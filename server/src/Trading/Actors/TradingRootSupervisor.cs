using Akka.Actor;
using Akka.DependencyInjection;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Repositories;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetPerUserTraderSupervisorProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopAllTradersForUserProtocol;

namespace Bogdabot.Server.Trading.Actors;

public class TradingRootSupervisor(
    DependencyResolver resolver,
    IActorTaskStoreFactory actorTaskStoreFactory,
    IUserRepository userRepository,
    ILogger<TradingRootSupervisor> logger
    ) : ReceiveActor, IWithUnboundedStash
{
    public IStash Stash { get; set; } = null!;

    protected enum RootSupervisorTask
    {
        StartingTraderSupervisor,
        StoppingTraderSupervisor,
    }

    private enum RootSupervisorPhase
    {
        Created,
        Initializing,
        Running
    }

    private DependencyResolver Resolver { get; } = resolver;

    protected ActorTaskStore<RootSupervisorTask> CurrentTasks { get; } = actorTaskStoreFactory.Create<RootSupervisorTask>();

    protected IUserRepository UserRepository { get; } = userRepository;

    protected ILogger<TradingRootSupervisor> Logger { get; } = logger;

    private RootSupervisorPhase CurrentPhase { get; set; } = RootSupervisorPhase.Created;

    #region External Messages
    public record SupervisorInitializedMessage
    {
        public required string ActorId;
    }
    #endregion

    #region Internal Messages
    private record InitializeInternalMessage { }
    private record InitializationFinishedInternalMessage { }

    #endregion

    /// <summary>
    /// Defines the default restart strategy for child traders while still allowing this actor
    /// to observe permanent termination through <see cref="Terminated" />.
    /// </summary>
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
        Become(PhaseInitializing);

        Self.Tell(new InitializeInternalMessage { });
    }

    private void PhaseInitializing()
    {
        CurrentPhase = RootSupervisorPhase.Initializing;

        ReceiveAsync<InitializeInternalMessage>(HandleInitialize);

        Receive<Terminated>(HandleTerminated);

        ReceiveAny(_ => Stash.Stash());
    }

    private async Task HandleInitialize(InitializeInternalMessage _)
    {
        Become(PhaseWaitingForSupervisorInitialization);

        try
        {
            // Start up trader supervisors for all users with active traders.
            foreach (var user in await UserRepository.GetAllUsersWithActiveTraders())
            {
                var actorId = PerUserTraderSupervisorId.FromUserGuid(user.Guid);

                var result = StartTraderSupervisor(
                    supervisorActorId: actorId,
                    userGuid: user.Guid,
                    sender: ActorRefs.Nobody
                );

                if (result is Result<IActorRef>.Error errorResult)
                {
                    Logger.LogError("Unable to create a trader supervisor for user {UserGuid}: {error}.", user.Guid, errorResult.ErrorValue.Reason);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to initialize trading root supervisor.");
        }

        if (CurrentTasks.IsEmpty())
        {
            FinishInitialization();
        }
    }

    private void PhaseWaitingForSupervisorInitialization()
    {
        CurrentPhase = RootSupervisorPhase.Initializing;

        Receive<SupervisorInitializedMessage>(HandleSupervisorInitialized);

        Receive<Terminated>(HandleTerminated);

        ReceiveAny(_ => Stash.Stash());
    }

    private void HandleSupervisorInitialized(SupervisorInitializedMessage message)
    {
        if (!CurrentTasks.HasTask(message.ActorId, RootSupervisorTask.StartingTraderSupervisor))
        {
            Logger.LogError("TradingRootSupervisor.HandleSupervisorInitialized: Error starting trader supervisor actor: there is currently no existing task running for starting the trader supervisor {ActorId}", message.ActorId);

            return;
        }

        if (CurrentTasks.GetSender(message.ActorId, RootSupervisorTask.StartingTraderSupervisor).Try(out IActorRef sender))
        {
            var traderSupervisor = Context.Child(message.ActorId);
            if (traderSupervisor.IsNobody())
            {
                Logger.LogError("TradingRootSupervisor.HandleSupervisorInitialized: Unknown error starting trader supervisor actor {ActorId}", message.ActorId);

                sender.Tell(new TraderSupervisorNotFoundResponse
                {
                    ActorId = message.ActorId
                });
            }
            else
            {
                sender.Tell(new TraderSupervisorFoundResponse
                {
                    TraderSupervisorReference = traderSupervisor
                });
            }
        }

        CurrentTasks.RemoveTask(
            actorId: message.ActorId,
            task: RootSupervisorTask.StartingTraderSupervisor
        );

        if (CurrentTasks.IsEmpty() && CurrentPhase == RootSupervisorPhase.Initializing)
        {
            FinishInitialization();
        }
    }

    private void PhaseRunning()
    {
        CurrentPhase = RootSupervisorPhase.Running;

        Receive<SupervisorInitializedMessage>(HandleSupervisorInitialized);

        Receive<InitializationFinishedInternalMessage>(HandleInitializationFinished);

        ReceiveAsync<GetTraderSupervisorRequest>(HandleGetOrCreateTraderSupervisor);

        Receive<StopAllTradersRequest>(HandleStopAllTradersForUserRequest);

        Receive<StopAllTradersResponse>(HandleStopAllTradersForUserResponse);

        Receive<Terminated>(HandleTerminated);
    }

    private void HandleInitializationFinished(InitializationFinishedInternalMessage _)
    {
        Logger.LogDebug("The trading root supervisor was successfully started.");

        Context.System.EventStream.Publish(new TradingInitializedEvent { });
    }

    private void FinishInitialization()
    {
        Become(PhaseRunning);

        Self.Tell(new InitializationFinishedInternalMessage { });

        Stash.UnstashAll();
    }

    private async Task HandleGetOrCreateTraderSupervisor(GetTraderSupervisorRequest request)
    {
        var actorId = PerUserTraderSupervisorId.FromUserGuid(request.UserGuid);
        var traderSupervisor = Context.Child(actorId);

        // Creates the supervisor if it does not already exist.
        if (traderSupervisor.IsNobody())
        {
            var result = StartTraderSupervisor(
                supervisorActorId: actorId,
                userGuid: request.UserGuid,
                sender: Sender
            );

            if (result is Result<IActorRef>.Error errorResult)
            {
                Logger.LogError("Unable to create a trader supervisor for user {UserGuid}: {error}.", request.UserGuid, errorResult.ErrorValue.Reason);

                Sender.Tell(new TraderSupervisorNotFoundResponse { ActorId = actorId });
            }

            // The new supervisor will be sent in HandleSupervisorInitialized() when it has been started. We can safely return here.

            return;
        }

        // If the supervisor already exists, we return it.
        Sender.Tell(new TraderSupervisorFoundResponse { TraderSupervisorReference = traderSupervisor });
    }

    private void HandleStopAllTradersForUserRequest(StopAllTradersRequest request)
    {
        var actorId = PerUserTraderSupervisorId.FromUserGuid(request.UserGuid);
        var traderSupervisor = Context.Child(actorId);

        if (traderSupervisor.IsNobody())
        {
            Logger.LogWarning("Unable to stop a trader supervisor for user {UserGuid}: the supervisor could not be found.", request.UserGuid);

            Sender.Tell(new AllTradersNotStoppedResponse
            {
                UserGuid = request.UserGuid,
                Error = new TraderSupervisorStopUserError($"No traders are running for the user {request.UserGuid}."),
            });

            return;
        }

        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            string errorMessage;
            if (existingTask is RootSupervisorTask.StoppingTraderSupervisor)
            {
                errorMessage = $"TradingRootSupervisor.StartSupervisor: A trader supervisor with the actor id {actorId} is already being stopped, ignoring duplicate stop request";
            }
            else
            {
                errorMessage = $"TradingRootSupervisor.StartSupervisor: Cannot stop trader supervisor because there is currently another task for the trader supervisor {actorId} active: {existingTask.GetType()}";
            }

            Sender.Tell(new AllTradersNotStoppedResponse
            {
                UserGuid = request.UserGuid,
                Error = new TraderSupervisorStopUserError(errorMessage),
            });

            return;
        }

        var addTaskResult = CurrentTasks.AddTask(
            actorId: actorId,
            task: RootSupervisorTask.StoppingTraderSupervisor,
            sender: Sender,
            workflowActor: ActorRefs.Nobody
        );

        if (addTaskResult is Result<RootSupervisorTask>.Error addTaskResultError)
        {
            Sender.Tell(new AllTradersNotStoppedResponse
            {
                UserGuid = request.UserGuid,
                Error = new TraderSupervisorStopUserError(addTaskResultError.ErrorValue),
            });

            return;
        }

        traderSupervisor.Tell(request);
    }

    protected void HandleStopAllTradersForUserResponse(StopAllTradersResponse response)
    {
        var actorId = PerUserTraderSupervisorId.FromUserGuid(response.UserGuid);

        if (!CurrentTasks.HasTask(actorId, RootSupervisorTask.StoppingTraderSupervisor))
        {
            Logger.LogError("TradingRootSupervisor.HandleStopAllTradersForUserResponse: Error stopping trader supervisor: there is currently no existing task running for stopping the trader supervisor for the user {UserGuid}", response.UserGuid);

            return;
        }

        if (CurrentTasks.GetSender(actorId, RootSupervisorTask.StoppingTraderSupervisor).Try(out IActorRef sender))
        {
            sender.Tell(response);
        }

        CurrentTasks.RemoveTask(actorId, RootSupervisorTask.StoppingTraderSupervisor);
    }

    protected void HandleTerminated(Terminated message)
    {
        var actorId = message.ActorRef.Path.Name;

        if (PerUserBrokerClientSupervisorId.TryParse(actorId, out _))
        {
            Logger.LogError("Broker client supervisor actor {actorId} has failed and is shut down.", actorId);

            return;
        }

        if (!PerUserTraderSupervisorId.TryParse(actorId, out _))
        {
            return;
        }

        // Prevent failed supervisors during initialization phase to ruin the whole startup.
        if (CurrentPhase == RootSupervisorPhase.Initializing && CurrentTasks.IsEmpty())
        {
            FinishInitialization();
        }

        // If there is a task, it most likely means that the actor has been shut down gracefully. It is unknown why the task hasn't been removed properly, but remove it anyway to clear the way and log the error.
        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            Logger.LogError("Trader supervisor actor {actorId} has a lingering task during termination: {task}. Removing the task now.", actorId, existingTask);

            if (existingTask is RootSupervisorTask.StartingTraderSupervisor
                && CurrentTasks.GetSender(actorId, RootSupervisorTask.StartingTraderSupervisor).Try(out IActorRef sender))
            {
                sender.Tell(new TraderSupervisorNotFoundResponse { ActorId = actorId });
            }

            CurrentTasks.RemoveTask(
                actorId: actorId
            );

            if (CurrentTasks.IsEmpty() && CurrentPhase == RootSupervisorPhase.Initializing)
            {
                FinishInitialization();
            }

            return;
        }

        Logger.LogError("Trader supervisor actor {actorId} has failed and is shut down.", actorId);
    }

    protected IActorRef GetOrCreateBrokerClientSupervisor(NonEmptyGuid userGuid)
    {
        var actorId = PerUserBrokerClientSupervisorId.FromUserGuid(userGuid);
        var brokerClientSupervisorReference = Context.Child(actorId);

        // Create the supervisor if it does not already exist.
        if (brokerClientSupervisorReference.IsNobody())
        {
            brokerClientSupervisorReference = Context.ActorOf(
                Resolver.Props<PerUserBrokerClientSupervisor>(userGuid), actorId
            );

            Context.Watch(brokerClientSupervisorReference);
        }

        return brokerClientSupervisorReference;
    }

    protected Result<IActorRef> StartTraderSupervisor(string supervisorActorId, NonEmptyGuid userGuid, IActorRef sender)
    {
        if (CurrentTasks.GetTask(supervisorActorId).Try(out var existingTask))
        {
            string errorMessage;
            if (existingTask is RootSupervisorTask.StartingTraderSupervisor)
            {
                errorMessage = $"TradingRootSupervisor.StartSupervisor: A trader supervisor with the actor id {supervisorActorId} is already being started, ignoring duplicate start request";
            }
            else
            {
                errorMessage = $"TradingRootSupervisor.StartSupervisor: Cannot start trader supervisor because there is currently another task for the trader supervisor {supervisorActorId} active: {existingTask.GetType()}";
            }

            return new Result<IActorRef>.Error(new ActorAlreadyHasTask(errorMessage));
        }

        var addTaskResult = CurrentTasks.AddTask(
            actorId: supervisorActorId,
            task: RootSupervisorTask.StartingTraderSupervisor,
            sender: sender,
            workflowActor: ActorRefs.Nobody
        );

        if (addTaskResult is Result<RootSupervisorTask>.Error addTaskResultError)
        {
            return new Result<IActorRef>.Error(addTaskResultError.ErrorValue);
        }

        try
        {
            var brokerClientSupervisorReference = GetOrCreateBrokerClientSupervisor(userGuid);

            var traderSupervisor = Context.ActorOf(
                Resolver.Props<PerUserTraderSupervisor>(userGuid, brokerClientSupervisorReference),
                supervisorActorId
            );

            Context.Watch(traderSupervisor);

            return new Result<IActorRef>.Ok(traderSupervisor);
        }
        catch (Exception e)
        {
            CurrentTasks.RemoveTask(supervisorActorId, RootSupervisorTask.StartingTraderSupervisor);

            return new Result<IActorRef>.Error(new TraderSupervisorStartUserError(e.ToString()));
        }
    }
}
