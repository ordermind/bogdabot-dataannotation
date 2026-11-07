using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.StateManagement;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.DeleteTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Trading.Actors.WorkflowActors;

public sealed class DeleteTraderWorkflowActor(
    ITraderLifecycleService lifecycleService,
    ITraderDatabaseConfigFactory traderDbConfigFactory,
    ILogger<DeleteTraderWorkflowActor> logger,
    TraderIdentifier traderIdentifier
    ) : ReceiveActor
{
    private ITraderLifecycleService LifecycleService { get; } = lifecycleService;
    private ITraderDatabaseConfigFactory TraderDbConfigFactory { get; } = traderDbConfigFactory;
    private ILogger<DeleteTraderWorkflowActor> Logger { get; } = logger;
    private TraderIdentifier TraderIdentifier { get; } = traderIdentifier;

    #region External messages
    public record StartWorkflowActor { }
    #endregion

    #region Internal messages
    private record StopActorInternalMessage { }
    private record CleanupDatabasesInternalMessage { }
    private record PersistStatusInternalMessage { }
    private record FinishSuccessfullyInternalMessage { }
    #endregion

    protected override void PreStart()
    {
        base.PreStart();

        Become(PhaseWaitingToStart);
    }

    private void PhaseWaitingToStart()
    {
        Receive<StartWorkflowActor>(HandleStartWorkflowActor);
    }

    private void HandleStartWorkflowActor(StartWorkflowActor _)
    {
        Become(PhaseStoppingActor);

        Self.Tell(new StopActorInternalMessage());
    }

    private void PhaseStoppingActor()
    {
        ReceiveAsync<StopActorInternalMessage>(HandleStopActorStep);
    }

    private async Task HandleStopActorStep(StopActorInternalMessage _)
    {
        Context.Parent.Tell(new StopTraderActorRequest
        {
            TraderIdentifier = TraderIdentifier
        });

        Become(PhaseWaitingForStopActorResponse);
    }

    private void PhaseWaitingForStopActorResponse()
    {
        ReceiveAsync<StopTraderActorResponse>(HandleStopTraderActorResponse);
    }

    private async Task HandleStopTraderActorResponse(StopTraderActorResponse response)
    {
        if (response.TraderIdentifier != TraderIdentifier)
        {
            Logger.LogError("A response for stopping a trader actor was sent to the wrong workflow actor. My identifier: {myIdentifier}. Identifier in the response: {theirIdentifier}", TraderIdentifier, response.TraderIdentifier);

            return;
        }

        if (response is TraderActorNotStoppedResponse errorResponse)
        {
            Logger.LogError("Failed to delete trader {identifier}. Reason: {reason}", TraderIdentifier, errorResponse.Error);

            Context.Parent.Tell(new TraderNotDeletedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = errorResponse.Error
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseCleanupDatabases);

        Self.Tell(new CleanupDatabasesInternalMessage { });
    }

    private void PhaseCleanupDatabases()
    {
        ReceiveAsync<CleanupDatabasesInternalMessage>(HandleCleanupDatabasesStep);
    }

    private async Task HandleCleanupDatabasesStep(CleanupDatabasesInternalMessage _)
    {
        var result = await CleanupDatabases(TraderIdentifier);

        if (result is Result<bool>.Error error)
        {
            Logger.LogError("Failed to clean up databases during deleting trader {identifier}. Reason: {reason}", TraderIdentifier, error.ErrorValue);

            Context.Parent.Tell(new TraderNotDeletedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = error.ErrorValue
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseFinishingSuccessfully);

        Self.Tell(new FinishSuccessfullyInternalMessage { });
    }

    private async Task<Result<bool>> CleanupDatabases(TraderIdentifier traderIdentifier)
    {
        try
        {
            await LifecycleService.DeleteTraderByGuid(traderIdentifier.TraderGuid);

            var traderDatabaseConfig = TraderDbConfigFactory.Create(
                userGuid: traderIdentifier.UserGuid,
                actorId: TraderActorId.FromTraderGuid(traderIdentifier.TraderGuid)
            );

            LifecycleService.DeleteTraderDatabase(traderDatabaseConfig);

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new FailedDatabaseCleanupError(e.ToString()));
        }
    }

    private void PhaseFinishingSuccessfully()
    {
        Receive<FinishSuccessfullyInternalMessage>(HandleFinishSuccessfullyStep);
    }

    private void HandleFinishSuccessfullyStep(FinishSuccessfullyInternalMessage _)
    {
        Logger.LogDebug("The trader {identifier} was successfully deleted.", TraderIdentifier);

        Context.Parent.Tell(new TraderDeletedResponse
        {
            TraderIdentifier = TraderIdentifier
        });

        Context.Stop(Self);
    }
}