using Akka.Actor;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.StartBrokerClientProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetBrokerClientProtocol;

namespace Bogdabot.Server.Trading.Actors;

public class PerUserBrokerClientSupervisor(
    IActorTaskStoreFactory actorTaskStoreFactory,
    ILoggerFactory loggerFactory,
    ILogger<PerUserBrokerClientSupervisor> logger,
    NonEmptyGuid userGuid
) : ReceiveActor, IWithUnboundedStash
{
    public IStash Stash { get; set; } = null!;

    private enum SupervisorTask
    {
        StartingBrokerClient,
    }

    private ILoggerFactory LoggerFactory { get; } = loggerFactory;

    private ILogger<PerUserBrokerClientSupervisor> Logger { get; } = logger;

    private NonEmptyGuid UserGuid { get; } = userGuid;

    private ActorTaskStore<SupervisorTask> CurrentTasks { get; } = actorTaskStoreFactory.Create<SupervisorTask>();

    #region Internal messages
    private record InitializeInternalMessage { }
    private record StartInternalMessage { }

    #endregion

    protected override void PreStart()
    {
        base.PreStart();

        Become(PhaseInitializing);

        Self.Tell(new InitializeInternalMessage());
    }

    private void PhaseInitializing()
    {
        ReceiveAsync<InitializeInternalMessage>(HandleInitialize);

        ReceiveAny(_ => Stash.Stash());
    }

    private async Task HandleInitialize(InitializeInternalMessage _)
    {
        var result = await Initialize();

        if (result is Result<bool>.Error error)
        {
            Logger.LogError("Error initializing broker client supervisor for user {UserGuid}: {message}", UserGuid, error.ErrorValue);

            Context.Stop(Self);

            return;
        }

        Become(PhaseRunning);

        Stash.UnstashAll();
    }

    private async Task<Result<bool>> Initialize()
    {
        try
        {
            // TODO: Any initializing procedures go here.

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new ActorInitializationError(e.ToString()));
        }
    }

    private void PhaseRunning()
    {
        Receive<GetBrokerClientActorRequest>(HandleGetBrokerClientActorRequest);
        Receive<StartBrokerClientActorResponse>(HandleStartBrokerClientActorResponse);

        Receive<Terminated>(HandleTerminated);
    }

    private void HandleGetBrokerClientActorRequest(GetBrokerClientActorRequest request)
    {
        if (request.Identifier.UserGuid != UserGuid)
        {
            Logger.LogError("A request for getting a broker actor was sent to the wrong broker supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, request.Identifier.UserGuid);

            Sender.Tell(new GetBrokerClientActorResponse
            {
                Result = new Result<IActorRef>.Error(new IllegalActorMessageError($"This request was sent to the wrong broker supervisor. My user guid: {UserGuid}. User guid in the request: {request.Identifier.UserGuid}"))
            });

            return;
        }

        var actorId = BrokerClientActorId.FromBrokerName(request.Identifier.BrokerName);

        var brokerClientActorReference = Context.Child(actorId);

        // If the broker client already exists, just return it.
        if (!brokerClientActorReference.IsNobody())
        {
            Sender.Tell(new GetBrokerClientActorResponse
            {
                Result = new Result<IActorRef>.Ok(brokerClientActorReference)
            });

            return;
        }

        var config = new BrokerClientConfiguration
        {
            UserGuid = UserGuid,
            BrokerName = request.Identifier.BrokerName
        };

        var result = StartBrokerClientActor(actorId, config);

        if (result is Result<bool>.Error error)
        {
            Sender.Tell(new GetBrokerClientActorResponse
            {
                Result = new Result<IActorRef>.Error(error.ErrorValue)
            });

            return;
        }

        // If the result is ok, a response will be sent later in the process so we don't need to do anything else here.
    }

    private Result<bool> StartBrokerClientActor(string actorId, BrokerClientConfiguration config)
    {
        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            if (existingTask == SupervisorTask.StartingBrokerClient)
            {
                Logger.LogWarning("PerUserBrokerClientSupervisor.StartBrokerClientActor: Duplicate start request for broker client {ActorId}", actorId);

                return new Result<bool>.Error(new BrokerClientStartUserError($"A broker client with the actor id {actorId} is already being started, ignoring duplicate start broker client request"));
            }

            Logger.LogWarning("PerUserBrokerClientSupervisor.StartBrokerClientActor: Cannot start broker client {ActorId} because there is currently another task active: {ActiveTask}", actorId, existingTask);

            return new Result<bool>.Error(new BrokerClientStartUserError($"Cannot start broker client because there is currently another task for the broker client {actorId} active: {existingTask}."));
        }

        var addTaskResult = CurrentTasks.AddTask(
            actorId: actorId,
            task: SupervisorTask.StartingBrokerClient,
            sender: Sender,
            workflowActor: ActorRefs.Nobody
        );

        if (addTaskResult is Result<SupervisorTask>.Error addTaskResultError)
        {
            Logger.LogError("PerUserBrokerClientSupervisor.StartBrokerClientActor: Could not add a task to start the broker client {actorId}. Reason: {reason}", actorId, addTaskResultError.ErrorValue);

            return new Result<bool>.Error(new BrokerClientStartInternalError(addTaskResultError.ErrorValue));
        }

        try
        {
            var logger = LoggerFactory.CreateLogger<BrokerClientActor>();

            var brokerClientActorReference = Context.ActorOf(
                Props.Create(() => new BrokerClientActor(
                    logger: logger,
                    config: config
            )), actorId);

            Context.Watch(brokerClientActorReference);

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new BrokerClientStartInternalError(e.ToString()));
        }
    }

    private void HandleStartBrokerClientActorResponse(StartBrokerClientActorResponse response)
    {
        if (response.Identifier.UserGuid != UserGuid)
        {
            Logger.LogError("A response for starting a broker actor was sent to the wrong broker supervisor. My user guid: {myUserGuid}. User guid in the request: {theirUserGuid}", UserGuid, response.Identifier.UserGuid);

            Sender.Tell(new GetBrokerClientActorResponse
            {
                Result = new Result<IActorRef>.Error(new IllegalActorMessageError($"This response was sent to the wrong broker supervisor. My user guid: {UserGuid}. User guid in the request: {response.Identifier.UserGuid}"))
            });

            return;
        }

        var actorId = BrokerClientActorId.FromBrokerName(response.Identifier.BrokerName);

        if (!CurrentTasks.HasTask(actorId, SupervisorTask.StartingBrokerClient))
        {
            Logger.LogError("PerUserBrokerClientSupervisor.HandleStartBrokerClientActorResponse: Error starting broker client actor: there is currently no existing task running for starting the broker client {Identifier}", response.Identifier);

            return;
        }

        Result<IActorRef> result = default!;
        if (response is BrokerClientActorNotStartedResponse errorResponse)
        {
            result = new Result<IActorRef>.Error(errorResponse.Error);
        }
        else if (response is BrokerClientActorStartedResponse)
        {
            var brokerClientActorReference = Context.Child(actorId);
            if (brokerClientActorReference.IsNobody())
            {
                result = new Result<IActorRef>.Error(new BrokerClientStartInternalError("Broker client is suddenly missing!"));
            }
            else
            {
                result = new Result<IActorRef>.Ok(brokerClientActorReference);
            }
        }

        if (CurrentTasks.GetSender(actorId, SupervisorTask.StartingBrokerClient).Try(out IActorRef sender))
        {
            sender.Tell(new GetBrokerClientActorResponse
            {
                Result = result
            });
        }
    }

    private void HandleTerminated(Terminated message)
    {
        if (message.ActorRef.GetType() != typeof(BrokerClientActor))
        {
            return;
        }

        var actorId = message.ActorRef.Path.Name;

        if (!BrokerClientActorId.TryParse(actorId, out var _))
        {
            Logger.LogError("Broker client actor {actorId} terminated with an invalid id.", actorId);
            return;
        }

        // If there is a task, it most likely means that the actor has been shut down gracefully. It is unknown why the task hasn't been removed properly, but remove it anyway to clear the way and log the error.
        if (CurrentTasks.GetTask(actorId).Try(out var existingTask))
        {
            Logger.LogError("Broker client actor {actorId} has a lingering task during termination: {task}. Removing the task now.", actorId, existingTask);

            CurrentTasks.RemoveTask(
                actorId: actorId
            );

            return;
        }

        Logger.LogError("Broker client actor {actorId} terminated permanently.", actorId);
    }
}