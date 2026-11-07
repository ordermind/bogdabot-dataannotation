using Akka.Actor;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Trading.Actors.WorkflowActors;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.DeleteTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopAllTradersForUserProtocol;
using Bogdabot.Server.Shared.Trading.Config;


namespace Bogdabot.Server.Trading.Actors;

internal sealed class PerUserTraderSupervisor(
    ITraderLifecycleService lifecycleService,
    TraderActorFactory traderActorFactory,
    TraderWorkflowActorFactory traderworkflowActorFactory,
    IActorTaskStoreFactory actorTaskStoreFactory,
    IActorRef brokerClientSupervisorReference,
    ILogger<PerUserTraderSupervisor> logger,
    NonEmptyGuid userGuid
    ) : ReceiveActor, IWithUnboundedStash
{
    public IStash Stash { get; set; } = null!;

    private enum SupervisorTask
    {
        StartingTrader,
        StoppingTrader,
        ArchivingTrader,
        DeletingTrader,
    }

    private enum SupervisorPhase
    {
        Created,
        Initializing,
        Running,
        Stopping,
    }

    private ITraderLifecycleService LifecycleService { get; } = lifecycleService;
    private TraderActorFactory TraderActorFactory { get; } = traderActorFactory;
    private TraderWorkflowActorFactory TraderWorkflowActorFactory { get; } = traderworkflowActorFactory;
    private ILogger<PerUserTraderSupervisor> Logger { get; } = logger;
    private NonEmptyGuid UserGuid { get; } = userGuid;
    private ActorTaskStore<SupervisorTask> CurrentTasks { get; } = actorTaskStoreFactory.Create<SupervisorTask>();
    private IActorRef BrokerClientSupervisorReference { get; } = brokerClientSupervisorReference;
    private SupervisorPhase CurrentPhase { get; set; } = SupervisorPhase.Created;

    #region Internal Messages

    private record InitializeInternalMessage { }
    private record StartActiveTraderInternalMessage : StartActiveTraderRequest { }

    private record StopTraderInternalMessage : StopTraderRequest { }

    #endregion

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 10,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: Akka.Actor.SupervisorStrategy.DefaultStrategy.Decider.Decide
        );
    }

    protected override void PreStart()
    {
        base.PreStart();

        Become(PhaseInitializing);

        Self.Tell(new InitializeInternalMessage());
    }

    private void PhaseInitializing()
    {
        CurrentPhase = SupervisorPhase.Initializing;

        ReceiveAsync<InitializeInternalMessage>(HandleInitialize);

        ReceiveAny(_ => Stash.Stash());
    }

    private async Task HandleInitialize(InitializeInternalMessage _)
    {
        var getActiveTradersResult = await GetActiveTradersFromDb();

        if (getActiveTradersResult is Result<IEnumerable<TraderIdentifier>>.Error error)
        {
            Logger.LogError("PerUserTraderSupervisor.HandleInitialize: Error fetching active traders for user {userGuid}. Reason: {reason}", UserGuid, error);

            Context.Stop(Self);

            return;
        }

        var ok = (Result<IEnumerable<TraderIdentifier>>.Ok)getActiveTradersResult;
        var activeTraders = ok.Value.ToArray();

        if (activeTraders.Length == 0)
        {
            FinishInitialization();

            return;
        }

        foreach (var traderIdentifier in activeTraders)
        {
            Self.Tell(new StartActiveTraderInternalMessage
            {
                TraderIdentifier = traderIdentifier
            });
        }

        Become(PhaseWaitingForTraderInitialization);
    }

    private void PhaseWaitingForTraderInitialization()
    {
        Receive<StartActiveTraderInternalMessage>(HandleStartActiveTraderInternally);
        Receive<StartTraderActorRequest>(HandleStartTraderActorRequest);
        Receive<StartTraderActorResponse>(HandleStartTraderActorResponse);
        Receive<GetTraderActorRequest>(HandleGetTraderActorRequest);
        ReceiveAsync<StartExistingTraderResponse>(HandleStartExistingTraderResponse);

        ReceiveAny(_ => Stash.Stash());
    }

    private void FinishInitialization()
    {
        Become(PhaseRunning);

        var actorId = PerUserTraderSupervisorId.FromUserGuid(UserGuid);

        Logger.LogDebug("The trader supervisor {actorId} was successfully started.", actorId);

        Context.Parent.Tell(new TradingRootSupervisor.SupervisorInitializedMessage { ActorId = actorId });

        Stash.UnstashAll();
    }

    private void PhaseRunning()
    {
        CurrentPhase = SupervisorPhase.Running;

        // Request handlers
        ReceiveAsync<StartNewTraderRequest>(HandleStartNewTraderRequest);

        Receive<StartActiveTraderInternalMessage>(HandleStartActiveTraderInternally);
        Receive<StartActiveTraderRequest>(HandleStartActiveTraderExternally);
        Receive<StartTraderActorRequest>(HandleStartTraderActorRequest);

        Receive<StopTraderRequest>(HandleStopOneTraderExternally);
        Receive<StopTraderInternalMessage>(HandleStopOneTraderInternally);
        Receive<StopTraderActorRequest>(HandleStopTraderActorRequest);

        Receive<DeleteTraderRequest>(HandleDeleteTraderRequest);

        Receive<GetTraderActorRequest>(HandleGetTraderActorRequest);

        ReceiveAsync<StopAllTradersRequest>(HandleStopAllTradersRequest);

        // Response handlers
        Receive<StartTraderActorResponse>(HandleStartTraderActorResponse);
        ReceiveAsync<StartExistingTraderResponse>(HandleStartExistingTraderResponse);

        Receive<StopTraderActorResponse>(HandleStopTraderActorResponse);
        Receive<StopTraderResponse>(HandleStopTraderResponse);

        Receive<DeleteTraderResponse>(HandleDeleteTraderResponse);

        // Special Terminated handler to handle failed child actors.
        ReceiveAsync<Terminated>(HandleTerminated);
    }

    #region Starting new traders

    private async Task HandleStartNewTraderRequest(StartNewTraderRequest request)
    {
        if (request.Config.UserGuid != UserGuid)
        {
            Logger.LogError("A request for starting a new trader was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.Config.UserGuid);

            Sender.Tell(new NewTraderNotStartedResponse
            {
                Config = request.Config,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.Config.UserGuid}"),
            });

            return;
        }

        var traderEntryResult = await CreateMainDbEntry(request.Config);

        if (traderEntryResult is Result<NonEmptyGuid>.Error error)
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStartNewTraderRequest: Error creating a database entry for new trader. Configuration: {config}. Reason: {reason}", request.Config, error.ErrorValue);

            Sender.Tell(new NewTraderNotStartedResponse
            {
                Config = request.Config,
                Error = error.ErrorValue,
            });

            return;
        }

        var ok = (Result<NonEmptyGuid>.Ok)traderEntryResult;
        var traderGuid = ok.Value;

        var startExistingTraderRequest = new StartActiveTraderRequest
        {
            TraderIdentifier = new TraderIdentifier
            {
                UserGuid = request.Config.UserGuid,
                TraderGuid = traderGuid
            }
        };

        // Deliberately call the method directly rather than send a message to preserve the sender.
        HandleStartActiveTraderRequest(startExistingTraderRequest, Sender);
    }

    #endregion

    #region Starting existing traders

    private void HandleStartActiveTraderInternally(StartActiveTraderInternalMessage message)
    {
        // Do not notify a sender because the request is sent internally.
        HandleStartActiveTraderRequest(message, ActorRefs.Nobody);
    }
    private void HandleStartActiveTraderExternally(StartActiveTraderRequest message)
    {
        HandleStartActiveTraderRequest(message, Sender);
    }
    private void HandleStartActiveTraderRequest(StartActiveTraderRequest request, IActorRef sender)
    {
        if (request.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStartActiveTraderRequest: A request for starting an active trader was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.TraderIdentifier.UserGuid);

            sender.Tell(new ExistingTraderNotStartedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(request.TraderIdentifier.TraderGuid);

        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            if (existingTask == SupervisorTask.StartingTrader)
            {
                Logger.LogWarning("PerUserTraderSupervisor.HandleStartActiveTraderRequest: Duplicate start request for trader {ActorId}", actorId);

                if (!sender.IsNobody())
                {
                    sender.Tell(new ExistingTraderNotStartedResponse
                    {
                        TraderIdentifier = request.TraderIdentifier,
                        Error = new TraderStartUserError($"A trader with the actor id {actorId} is already being started, ignoring duplicate start trader request"),
                    });
                }

                return;
            }

            Logger.LogWarning("PerUserTraderSupervisor.HandleStartActiveTraderRequest: Cannot start trader {ActorId} because there is currently another task active: {ActiveTask}", actorId, existingTask);

            if (!sender.IsNobody())
            {
                sender.Tell(new ExistingTraderNotStartedResponse
                {
                    TraderIdentifier = request.TraderIdentifier,
                    Error = new TraderStartUserError($"Cannot start trader because there is currently another task for the trader {actorId} active: {existingTask}."),
                });
            }

            return;
        }

        var workflowActor = TraderWorkflowActorFactory.CreateStartExistingTraderWorkFlowActor(Context, request.TraderIdentifier);

        var addTaskResult = CurrentTasks.AddTask(
            actorId: actorId,
            task: SupervisorTask.StartingTrader,
            sender: sender,
            workflowActor: workflowActor
        );

        if (addTaskResult is Result<SupervisorTask>.Error addTaskResultError)
        {
            Context.Stop(workflowActor);

            Logger.LogError("PerUserTraderSupervisor.HandleStartActiveTraderRequest: Could not add a task to start the trader {actorId}. Reason: {reason}", actorId, addTaskResultError.ErrorValue.Reason);

            if (!sender.IsNobody())
            {
                sender.Tell(new ExistingTraderNotStartedResponse
                {
                    TraderIdentifier = request.TraderIdentifier,
                    Error = new TraderStartInternalError(addTaskResultError.ErrorValue),
                });
            }

            return;
        }

        workflowActor.Tell(new StartExistingTraderWorkflowActor.StartWorkflowActor { });
    }

    /// <summary>
    /// Sent from StartExistingTraderWorkflowActor when it's ready to start the trader actor.
    /// </summary>
    /// <param name="request"></param>
    private void HandleStartTraderActorRequest(StartTraderActorRequest request)
    {
        if (request.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A request for starting a trader actor was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.TraderIdentifier.UserGuid);

            Sender.Tell(new TraderActorNotStartedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Config = request.Config,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(request.TraderIdentifier.TraderGuid);

        if (!CurrentTasks.HasTask(actorId, SupervisorTask.StartingTrader))
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStartTraderActorRequest: Error starting trader actor: there is currently no existing task running for starting the trader {TraderIdentifier}", request.TraderIdentifier);

            return;
        }

        var traderActor = TraderActorFactory.Create(Context, request.Config, request.TraderIdentifier);
        Context.Watch(traderActor);
    }

    /// <summary>
    /// This is sent by TraderActor after it attempts to start.
    /// </summary>
    /// <param name="response"></param>
    private void HandleStartTraderActorResponse(StartTraderActorResponse response)
    {
        if (response.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A response for starting a trader actor was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, response.TraderIdentifier.UserGuid);

            Sender.Tell(new TraderActorNotStartedResponse
            {
                TraderIdentifier = response.TraderIdentifier,
                Config = response.Config,
                Error = new IllegalActorMessageError($"This response was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {response.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(response.TraderIdentifier.TraderGuid);

        if (!CurrentTasks.HasTask(actorId, SupervisorTask.StartingTrader))
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStartTraderActorResponse: Error starting trader actor: there is currently no existing task running for starting the trader {TraderIdentifier}", response.TraderIdentifier);

            return;
        }

        if (CurrentTasks.GetWorkflowActor(actorId, SupervisorTask.StartingTrader).Try(out IActorRef workflowActor))
        {
            workflowActor.Tell(response);
        }
    }

    /// <summary>
    /// Sent by StartExistingTraderWorkflowActor, a decisive message regarding whether or not starting the trader succeeded.
    /// </summary>
    /// <param name="response"></param>
    private async Task HandleStartExistingTraderResponse(StartExistingTraderResponse response)
    {
        if (response.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A response for starting an existing trader was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, response.TraderIdentifier.UserGuid);

            Sender.Tell(new ExistingTraderNotStartedResponse
            {
                TraderIdentifier = response.TraderIdentifier,
                Error = new IllegalActorMessageError($"This response was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {response.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var traderGuid = response.TraderIdentifier.TraderGuid;
        var actorId = TraderActorId.FromTraderGuid(traderGuid);

        if (!CurrentTasks.HasTask(actorId, SupervisorTask.StartingTrader))
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStartTraderResponse: Error starting trader actor: there is currently no existing task running for starting the trader {TraderIdentifier}", response.TraderIdentifier);

            return;
        }

        if (CurrentTasks.GetSender(actorId, SupervisorTask.StartingTrader).Try(out IActorRef sender))
        {
            sender.Tell(response);
        }

        CurrentTasks.RemoveTask(actorId, SupervisorTask.StartingTrader);

        if (CurrentPhase == SupervisorPhase.Initializing && CurrentTasks.IsEmpty())
        {
            FinishInitialization();
        }
    }

    #endregion

    #region Stopping traders

    private async Task HandleStopAllTradersRequest(StopAllTradersRequest request)
    {
        if (request.UserGuid != UserGuid)
        {
            Logger.LogError("A request for stopping all traders was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.UserGuid);

            Sender.Tell(new AllTradersNotStoppedResponse
            {
                UserGuid = request.UserGuid,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.UserGuid}"),
            });

            return;
        }

        var getActiveTradersResult = await GetActiveTradersFromDb();

        if (getActiveTradersResult is Result<IEnumerable<TraderIdentifier>>.Error error)
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStopAllTradersRequest: Error fetching active traders for user {userGuid}. Reason: {reason}", UserGuid, error);

            Context.Parent.Tell(new AllTradersNotStoppedResponse
            {
                UserGuid = UserGuid,
                Error = error.ErrorValue
            });

            return;
        }

        var ok = (Result<IEnumerable<TraderIdentifier>>.Ok)getActiveTradersResult;
        var activeTraders = ok.Value.ToArray();

        if (activeTraders.Length == 0)
        {
            Context.Parent.Tell(new AllTradersStoppedResponse
            {
                UserGuid = UserGuid
            });

            Context.Stop(Self);

            return;
        }

        foreach (var traderIdentifier in activeTraders)
        {
            Self.Tell(new StopTraderInternalMessage
            {
                TraderIdentifier = traderIdentifier
            });
        }

        Become(PhaseStopping);
    }

    private void HandleStopOneTraderInternally(StopTraderInternalMessage message)
    {
        // Do not notify a sender because the request is sent internally.
        HandleStopOneTraderRequest(message, ActorRefs.Nobody);
    }
    private void HandleStopOneTraderExternally(StopTraderRequest message)
    {
        HandleStopOneTraderRequest(message, Sender);
    }

    private void HandleStopOneTraderRequest(StopTraderRequest request, IActorRef sender)
    {
        if (request.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A request for stopping a trader was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.TraderIdentifier.UserGuid);

            sender.Tell(new TraderNotStoppedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(request.TraderIdentifier.TraderGuid);

        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            if (existingTask == SupervisorTask.StoppingTrader)
            {
                Logger.LogWarning("PerUserTraderSupervisor.HandleStopTraderRequest: Duplicate stop request for trader {ActorId}", actorId);

                if (!sender.IsNobody())
                {
                    sender.Tell(new TraderNotStoppedResponse
                    {
                        TraderIdentifier = request.TraderIdentifier,
                        Error = new TraderStopUserError($"A trader with the actor id {actorId} is already being stopped, ignoring duplicate stop trader request"),
                    });
                }

                return;
            }

            Logger.LogWarning("PerUserTraderSupervisor.HandleStopTraderRequest: Cannot stop trader {ActorId} because there is currently another task active: {ActiveTask}", actorId, existingTask);

            if (!sender.IsNobody())
            {
                sender.Tell(new TraderNotStoppedResponse
                {
                    TraderIdentifier = request.TraderIdentifier,
                    Error = new TraderStopUserError($"Cannot stop trader because there is currently another task for the trader {actorId} active: {existingTask}."),
                });
            }

            return;
        }

        if (BrokerClientSupervisorReference.IsNobody())
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStopTraderRequest: The broker client supervisor actor for user {userGuid} could not be found, shutting down...", UserGuid);

            Context.Stop(Self);

            return;
        }

        var workflowActor = TraderWorkflowActorFactory.CreateStopTraderWorkFlowActor(
            Context,
            request.TraderIdentifier,
            BrokerClientSupervisorReference
        );

        var addTaskResult = CurrentTasks.AddTask(
            actorId: actorId,
            task: SupervisorTask.StoppingTrader,
            sender: sender,
            workflowActor: workflowActor
        );

        if (addTaskResult is Result<SupervisorTask>.Error addTaskResultError)
        {
            Context.Stop(workflowActor);

            Logger.LogError("PerUserTraderSupervisor.HandleStopTraderRequest: Could not add a task to stop the trader {actorId}. Reason: {reason}", actorId, addTaskResultError.ErrorValue.Reason);

            if (!sender.IsNobody())
            {
                sender.Tell(new TraderNotStoppedResponse
                {
                    TraderIdentifier = request.TraderIdentifier,
                    Error = new TraderStopInternalError(addTaskResultError.ErrorValue),
                });
            }

            return;
        }

        workflowActor.Tell(new StopTraderWorkflowActor.StartWorkflowActor { });
    }

    /// <summary>
    /// Sent from StopTraderWorkflowActor, ArchiveTraderWorkflowActor or DeleteTraderWorkflowActor when it's ready to stop the trader actor.
    /// </summary>
    /// <param name="request"></param>
    private void HandleStopTraderActorRequest(StopTraderActorRequest request)
    {
        if (request.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A request for stopping a trader actor was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.TraderIdentifier.UserGuid);

            Sender.Tell(new TraderActorNotStoppedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(request.TraderIdentifier.TraderGuid);

        var allowedTasks = new[] { SupervisorTask.StoppingTrader, SupervisorTask.ArchivingTrader, SupervisorTask.DeletingTrader };

        if (!CurrentTasks.HasTaskOf(actorId, allowedTasks))
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStopTraderActorRequest: Error stopping trader actor: there is currently no existing task running for stopping/archiving/deleting the trader {TraderIdentifier}", request.TraderIdentifier);

            return;
        }

        var traderActor = Context.Child(actorId);
        if (traderActor.IsNobody())
        {
            // The trader actor is already stopped, do a noop and just send a success message back to myself just like the trader would.
            Self.Tell(new TraderActorStoppedResponse
            {
                TraderIdentifier = request.TraderIdentifier
            });
        }
        else
        {
            traderActor.Tell(request);
        }
    }

    /// <summary>
    /// This is sent by TraderActor after it attempts to stop.
    /// </summary>
    /// <param name="response"></param>
    private void HandleStopTraderActorResponse(StopTraderActorResponse response)
    {
        if (response.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A response for stopping a trader actor was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, response.TraderIdentifier.UserGuid);

            Sender.Tell(new TraderActorNotStoppedResponse
            {
                TraderIdentifier = response.TraderIdentifier,
                Error = new IllegalActorMessageError($"This response was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {response.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(response.TraderIdentifier.TraderGuid);

        var allowedTasks = new[] { SupervisorTask.StoppingTrader, SupervisorTask.ArchivingTrader, SupervisorTask.DeletingTrader };

        if (!CurrentTasks.HasTaskOf(actorId, allowedTasks))
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStopTraderActorResponse: Error stopping trader actor: there is currently no existing task running for stopping/archiving/deleting the trader {TraderIdentifier}", response.TraderIdentifier);

            return;
        }

        if (
            CurrentTasks.GetTask(actorId).Try(out var task)
            && CurrentTasks.GetWorkflowActor(actorId, task).Try(out IActorRef workflowActor)
        )
        {
            workflowActor.Tell(response);
        }
    }

    /// <summary>
    /// Sent by StopTraderWorkflowActor, a decisive message regarding whether or not stopping the trader succeeded.
    /// </summary>
    /// <param name="response"></param>
    private void HandleStopTraderResponse(StopTraderResponse response)
    {
        if (response.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A response for stopping a trader was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, response.TraderIdentifier.UserGuid);

            Sender.Tell(new TraderNotStoppedResponse
            {
                TraderIdentifier = response.TraderIdentifier,
                Error = new IllegalActorMessageError($"This response was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {response.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(response.TraderIdentifier.TraderGuid);

        if (!CurrentTasks.HasTask(actorId, SupervisorTask.StoppingTrader))
        {
            Logger.LogError("PerUserTraderSupervisor.HandleStopTraderResponse: Error stopping trader: there is currently no existing task running for stopping the trader {TraderIdentifier}", response.TraderIdentifier);

            return;
        }

        if (CurrentTasks.GetSender(actorId, SupervisorTask.StoppingTrader).Try(out IActorRef sender))
        {
            sender.Tell(response);
        }

        CurrentTasks.RemoveTask(actorId, SupervisorTask.StoppingTrader);

        if (CurrentPhase == SupervisorPhase.Stopping && CurrentTasks.IsEmpty())
        {
            FinishStopping();
        }
    }

    #endregion

    #region Deleting traders

    private void HandleDeleteTraderRequest(DeleteTraderRequest request)
    {
        if (request.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A request for deleting a trader was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.TraderIdentifier.UserGuid);

            Sender.Tell(new TraderNotDeletedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(request.TraderIdentifier.TraderGuid);

        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            if (existingTask == SupervisorTask.DeletingTrader)
            {
                Logger.LogWarning("PerUserTraderSupervisor.HandleDeleteTraderRequest: Duplicate delete request for trader {ActorId}", actorId);

                Sender.Tell(new TraderNotDeletedResponse
                {
                    TraderIdentifier = request.TraderIdentifier,
                    Error = new TraderDeleteUserError($"A trader with the actor id {actorId} is already being deleted, ignoring duplicate delete trader request"),
                });

                return;
            }

            Logger.LogError("PerUserTraderSupervisor.HandleDeleteTraderRequest: Cannot delete trader {ActorId} because there is currently another task active: {ActiveTask}", actorId, existingTask);

            Sender.Tell(new TraderNotDeletedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Error = new TraderDeleteUserError($"Cannot delete trader because there is currently another task for the trader {actorId} active: {existingTask}."),
            });

            return;
        }

        var workflowActor = TraderWorkflowActorFactory.CreateDeleteTraderWorkFlowActor(Context, request.TraderIdentifier);

        var addTaskResult = CurrentTasks.AddTask(
            actorId: actorId,
            task: SupervisorTask.DeletingTrader,
            sender: Sender,
            workflowActor: workflowActor
        );

        if (addTaskResult is Result<SupervisorTask>.Error addTaskResultError)
        {
            Context.Stop(workflowActor);

            Logger.LogError("PerUserTraderSupervisor.HandleDeleteTraderRequest: Could not add a task to delete the trader {actorId}. Reason: {reason}", actorId, addTaskResultError.ErrorValue.Reason);

            Sender.Tell(new TraderNotDeletedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Error = new TraderDeleteInternalError(addTaskResultError.ErrorValue),
            });

            return;
        }

        workflowActor.Tell(new DeleteTraderWorkflowActor.StartWorkflowActor { });
    }

    /// <summary>
    /// Sent by DeleteTraderWorkflowActor, a decisive message regarding whether or not deleting the trader succeeded.
    /// </summary>
    /// <param name="response"></param>
    private void HandleDeleteTraderResponse(DeleteTraderResponse response)
    {
        if (response.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A response for deleting a trader was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, response.TraderIdentifier.UserGuid);

            Sender.Tell(new TraderNotDeletedResponse
            {
                TraderIdentifier = response.TraderIdentifier,
                Error = new IllegalActorMessageError($"This response was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {response.TraderIdentifier.UserGuid}"),
            });

            return;
        }

        var actorId = TraderActorId.FromTraderGuid(response.TraderIdentifier.TraderGuid);

        if (!CurrentTasks.HasTask(actorId, SupervisorTask.DeletingTrader))
        {
            Logger.LogError("PerUserTraderSupervisor.HandleDeleteTraderResponse: Error deleting trader: there is currently no existing task running for deleting the trader {TraderIdentifier}", response.TraderIdentifier);

            return;
        }

        if (CurrentTasks.GetSender(actorId, SupervisorTask.DeletingTrader).Try(out IActorRef sender))
        {
            sender.Tell(response);
        }

        CurrentTasks.RemoveTask(actorId, SupervisorTask.DeletingTrader);
    }

    #endregion

    #region Getting traders
    private void HandleGetTraderActorRequest(GetTraderActorRequest request)
    {
        if (request.TraderIdentifier.UserGuid != UserGuid)
        {
            Logger.LogError("A request for getting a trader actor was sent to the wrong trader supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.TraderIdentifier.UserGuid);

            Sender.Tell(new GetTraderActorResponse
            {
                Result = new Result<IActorRef>.Error(new IllegalActorMessageError($"This request was sent to the wrong trader supervisor. My user guid: {UserGuid}. User guid in the request: {request.TraderIdentifier.UserGuid}"))
            });

            return;
        }

        var traderActor = Context.Child(TraderActorId.FromTraderGuid(request.TraderIdentifier.TraderGuid));
        if (traderActor.IsNobody())
        {
            Sender.Tell(new GetTraderActorResponse
            {
                Result = new Result<IActorRef>.Error(new TraderActorNotFoundError("The trader actor could not be found in the registry."))
            });

            return;
        }

        Sender.Tell(new GetTraderActorResponse
        {
            Result = new Result<IActorRef>.Ok(traderActor)
        });
    }
    #endregion

    private void PhaseStopping()
    {
        CurrentPhase = SupervisorPhase.Stopping;

        Receive<StopTraderInternalMessage>(HandleStopOneTraderInternally);
        Receive<StopTraderActorRequest>(HandleStopTraderActorRequest);

        Receive<StopTraderActorResponse>(HandleStopTraderActorResponse);
        Receive<StopTraderResponse>(HandleStopTraderResponse);

        ReceiveAsync<Terminated>(HandleTerminated);
    }

    private void FinishStopping()
    {
        var actorId = PerUserTraderSupervisorId.FromUserGuid(UserGuid);

        Context.Stop(Self);

        Logger.LogDebug("The trader supervisor {actorId} was successfully stopped and all its traders have been permanently stopped as well.", actorId);

        Context.Parent.Tell(new AllTradersStoppedResponse
        {
            UserGuid = UserGuid
        });
    }

    private async Task<Result<IEnumerable<TraderIdentifier>>> GetActiveTradersFromDb()
    {
        try
        {
            var activeTraders = await LifecycleService.GetActiveTradersForUser(UserGuid);

            return new Result<IEnumerable<TraderIdentifier>>.Ok(activeTraders);
        }
        catch (Exception e)
        {
            return new Result<IEnumerable<TraderIdentifier>>.Error(new FailedDatabaseReadError(e.ToString()));
        }
    }
    private async Task<Result<NonEmptyGuid>> CreateMainDbEntry(TraderConfiguration traderConfig)
    {
        try
        {
            // TODO: The initial price should be supplied by connecting to the broker and fetching the current price, in the meantime we will just put 0m.
            var traderGuid = await LifecycleService.CreateTrader(traderConfig, 0m);

            return new Result<NonEmptyGuid>.Ok(traderGuid);
        }
        catch (Exception e)
        {
            return new Result<NonEmptyGuid>.Error(new FailedDatabaseWriteError(e.ToString()));
        }
    }
    private async Task HandleTerminated(Terminated message)
    {
        if (message.ActorRef.GetType() != typeof(TraderActor))
        {
            return;
        }

        var actorId = message.ActorRef.Path.Name;

        if (!TraderActorId.TryParse(actorId, out var traderGuid))
        {
            Logger.LogError("Trader actor {actorId} terminated with an invalid id.", actorId);
            return;
        }

        // If there is a task, it most likely means that the actor has been shut down gracefully. It is unknown why the task hasn't been removed properly, but remove it anyway to clear the way and log the error.
        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            Logger.LogError("Trader actor {actorId} has a lingering task during termination: {task}. Removing the task now.", actorId, existingTask);

            CurrentTasks.RemoveTask(
                actorId: actorId
            );

            return;
        }

        Logger.LogError("Trader actor {actorId} terminated permanently and will be marked failed.", actorId);

        await MarkTraderFailed(traderGuid);
    }

    private async Task MarkTraderFailed(NonEmptyGuid traderGuid)
    {
        try
        {
            await LifecycleService.MarkTraderFailed(traderGuid);
        }
        catch (Exception e)
        {
            Logger.LogCritical("Error changing trader status to Failed: {message}", e);
        }

    }
}
