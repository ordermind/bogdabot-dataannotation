using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.StateManagement;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Trading.Actors.WorkflowActors;

public sealed class StartExistingTraderWorkflowActor(
        ITraderDatabaseConfigFactory traderDbConfigFactory,
        ITraderDbMigrationService traderMigrationService,
        ITraderLifecycleService lifecycleService,
        ILogger<StartExistingTraderWorkflowActor> logger,
        TraderIdentifier traderIdentifier
) : ReceiveActor
{
    private ITraderDatabaseConfigFactory TraderDbConfigFactory { get; } = traderDbConfigFactory;
    private ITraderDbMigrationService TraderMigrationService { get; } = traderMigrationService;
    private ITraderLifecycleService LifecycleService { get; } = lifecycleService;
    private ILogger<StartExistingTraderWorkflowActor> Logger { get; } = logger;
    private TraderIdentifier TraderIdentifier { get; } = traderIdentifier;

    private TraderConfiguration? TraderConfig { get; set; } = null;

    private static readonly TraderStatusEnum[] StartableStatuses =
    [
        TraderStatusEnum.Created,
        TraderStatusEnum.Failed,
    ];

    #region External messages
    public record StartWorkflowActor { }
    #endregion

    #region Internal messages
    private record ValidateStatusChangeInternalMessage { }
    private record RunTraderMigrationsInternalMessage { }
    private record FetchTraderConfigInternalMessage { }
    private record StartBrokerClientActorInternalMessage { }
    private record StartTraderActorInternalMessage { }
    private record PersistStatusInternalMessage { }
    private record FinishSuccessfullyInternalMessage { }
    private record AbortInternalMessage
    {
        public required AbstractError Error { get; init; }
    }
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
        Become(PhaseValidatingStatusChange);

        Self.Tell(new ValidateStatusChangeInternalMessage { });
    }

    private void PhaseValidatingStatusChange()
    {
        ReceiveAsync<ValidateStatusChangeInternalMessage>(HandleValidateStatusChangeStep);
    }

    private async Task HandleValidateStatusChangeStep(ValidateStatusChangeInternalMessage _)
    {
        var currentStatusResult = await GetCurrentStatus();
        if (currentStatusResult is Result<TraderStatusEnum>.Error currentStatusError)
        {
            Logger.LogError("Error getting the current status for a trader. Identifier: {identifier}. Reason: {reason}", TraderIdentifier, currentStatusError.ErrorValue);

            Become(PhaseAborting);

            Self.Tell(new AbortInternalMessage
            {
                Error = currentStatusError.ErrorValue
            });

            return;
        }

        var currentStatusOk = (Result<TraderStatusEnum>.Ok)currentStatusResult;
        var oldStatus = currentStatusOk.Value;

        if (oldStatus == TraderStatusEnum.Running)
        {
            if (await IsTraderActorRunning())
            {
                Logger.LogWarning("The trader {identifier} is already started, I will do a noop", TraderIdentifier);

                Context.Parent.Tell(new ExistingTraderStartedResponse
                {
                    TraderIdentifier = TraderIdentifier
                });

                Context.Stop(Self);

                return;
            }
        }
        else if (!StartableStatuses.Contains(oldStatus))
        {
            Context.Parent.Tell(new ExistingTraderNotStartedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = new IllegalStatusTransitionError($"You cannot change the trader status from {oldStatus} to {TraderStatusEnum.Running}")
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseRunningTraderMigrations);

        Self.Tell(new RunTraderMigrationsInternalMessage { });
    }

    private async Task<Result<TraderStatusEnum>> GetCurrentStatus()
    {
        try
        {
            var traderStatus = await LifecycleService.GetCurrentStatus(TraderIdentifier.TraderGuid);

            return new Result<TraderStatusEnum>.Ok(traderStatus);
        }
        catch (Exception e)
        {
            return new Result<TraderStatusEnum>.Error(new FailedStatusFetchError(e.ToString()));
        }
    }

    private async Task<bool> IsTraderActorRunning()
    {
        var response = await Context.Parent.Ask<GetTraderActorResponse>(
            new GetTraderActorRequest { TraderIdentifier = TraderIdentifier }
        );

        return response.Result is Result<IActorRef>.Ok;
    }

    private void PhaseRunningTraderMigrations()
    {
        ReceiveAsync<RunTraderMigrationsInternalMessage>(HandleRunTraderMigrationsStep);
    }

    private async Task HandleRunTraderMigrationsStep(RunTraderMigrationsInternalMessage _)
    {
        var runMigrationsResult = await RunTraderMigrations(TraderIdentifier);

        if (runMigrationsResult is Result<bool>.Error runMigrationsError)
        {
            Logger.LogError("Error running migrations for trader. Identifier: {identifier}. Reason: {reason}", TraderIdentifier, runMigrationsError.ErrorValue);

            Become(PhaseAborting);

            Self.Tell(new AbortInternalMessage
            {
                Error = runMigrationsError.ErrorValue
            });

            return;
        }

        Become(PhaseFetchTraderConfig);

        Self.Tell(new FetchTraderConfigInternalMessage());
    }

    private async Task<Result<bool>> RunTraderMigrations(TraderIdentifier traderIdentifier)
    {
        try
        {
            var databaseConfig = TraderDbConfigFactory.Create(
                userGuid: traderIdentifier.UserGuid,
                actorId: TraderActorId.FromTraderGuid(traderIdentifier.TraderGuid)
            );

            await TraderMigrationService.RunMigrations(databaseConfig);

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new FailedDatabaseMigrationsError(e.ToString()));
        }
    }

    private void PhaseFetchTraderConfig()
    {
        ReceiveAsync<FetchTraderConfigInternalMessage>(HandleFetchTraderConfigStep);
    }

    private async Task HandleFetchTraderConfigStep(FetchTraderConfigInternalMessage _)
    {
        var result = await FetchTraderConfig();
        if (result is Result<TraderConfiguration>.Error error)
        {
            Logger.LogError("Failed to fetch trader configuration in order to start trader {identifier}. Reason: {reason}", TraderIdentifier, error.ErrorValue);

            Become(PhaseAborting);

            Self.Tell(new AbortInternalMessage
            {
                Error = error.ErrorValue
            });

            return;
        }

        var ok = (Result<TraderConfiguration>.Ok)result;
        TraderConfig = ok.Value;

        Become(PhaseStartingBrokerClientActor);

        Self.Tell(new StartBrokerClientActorInternalMessage { });
    }

    private async Task<Result<TraderConfiguration>> FetchTraderConfig()
    {
        try
        {
            var traderConfig = await LifecycleService.GetTraderConfiguration(TraderIdentifier.TraderGuid);

            return new Result<TraderConfiguration>.Ok(traderConfig);
        }
        catch (Exception e)
        {
            return new Result<TraderConfiguration>.Error(new FailedConfigFetchError(e.ToString()));
        }
    }

    private void PhaseStartingBrokerClientActor()
    {
        ReceiveAsync<StartBrokerClientActorInternalMessage>(HandleStartBrokerClientActorStep);
    }

    private async Task HandleStartBrokerClientActorStep(StartBrokerClientActorInternalMessage message)
    {
        // TODO

        Become(PhaseStartingTraderActor);

        Self.Tell(new StartTraderActorInternalMessage { });
    }

    private void PhaseStartingTraderActor()
    {
        ReceiveAsync<StartTraderActorInternalMessage>(HandleStartTraderActorStep);
    }

    private async Task HandleStartTraderActorStep(StartTraderActorInternalMessage _)
    {
        Context.Parent.Tell(new StartTraderActorRequest
        {
            Config = TraderConfig ?? throw new InvalidOperationException(
                    "TraderConfig must have been set before receiving StartTraderActorInternalMessage."),
            TraderIdentifier = TraderIdentifier
        });

        Become(PhaseWaitingForStartTraderActorResponse);
    }

    private void PhaseWaitingForStartTraderActorResponse()
    {
        ReceiveAsync<StartTraderActorResponse>(HandleStartTraderActorResponse);
    }

    private async Task HandleStartTraderActorResponse(StartTraderActorResponse response)
    {
        if (response.TraderIdentifier != TraderIdentifier)
        {
            Logger.LogError("A response for starting a trader actor was sent to the wrong workflow actor. My identifier: {myIdentifier}. Identifier in the response: {theirIdentifier}", TraderIdentifier, response.TraderIdentifier);

            return;
        }

        if (response is TraderActorNotStartedResponse errorResponse)
        {
            Logger.LogError("Failed to start the actor for trader {identifier}. Reason: {reason}", TraderIdentifier, errorResponse.Error);

            Become(PhaseAborting);

            Self.Tell(new AbortInternalMessage
            {
                Error = errorResponse.Error
            });

            return;
        }

        // We always persist the trader so that the last_started column can be updated.
        Become(PhasePersistingNewStatus);

        Self.Tell(new PersistStatusInternalMessage { });
    }

    private void PhasePersistingNewStatus()
    {
        ReceiveAsync<PersistStatusInternalMessage>(HandlePersistStatusStep);
    }

    private async Task HandlePersistStatusStep(PersistStatusInternalMessage _)
    {
        var result = await PersistRunningStatus();
        if (result is Result<bool>.Error error)
        {
            Logger.LogError(
                "Trader {identifier} reported started, but status could not be updated: {message}",
                TraderIdentifier,
                error.ErrorValue
            );

            Become(PhaseAborting);

            Self.Tell(new AbortInternalMessage
            {
                Error = error.ErrorValue
            });

            return;
        }

        Become(PhaseFinishingSuccessfully);

        Self.Tell(new FinishSuccessfullyInternalMessage { });
    }

    private async Task<Result<bool>> PersistRunningStatus()
    {
        try
        {
            await LifecycleService.MarkTraderRunning(TraderIdentifier.TraderGuid);

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new FailedPersistingStoppedTraderError(e.ToString()));
        }
    }

    private void PhaseFinishingSuccessfully()
    {
        Receive<FinishSuccessfullyInternalMessage>(HandleFinishSuccessfullyStep);
    }

    private void HandleFinishSuccessfullyStep(FinishSuccessfullyInternalMessage _)
    {
        Logger.LogDebug("The trader {identifier} was successfully started.", TraderIdentifier);

        Context.Parent.Tell(new ExistingTraderStartedResponse
        {
            TraderIdentifier = TraderIdentifier
        });

        Context.Stop(Self);
    }

    private void PhaseAborting()
    {
        ReceiveAsync<AbortInternalMessage>(HandleAbort);
    }

    private async Task HandleAbort(AbortInternalMessage message)
    {
        // First, stop the trader actor if it is running
        if (await IsTraderActorRunning())
        {
            var response = (StopTraderActorResponse)await Context.Parent.Ask(new StopTraderActorRequest
            {
                TraderIdentifier = TraderIdentifier
            });

            if (response is TraderActorNotStoppedResponse errorResponse)
            {
                Logger.LogError("Failed to stop trader actor {identifier}. Reason: {reason}", TraderIdentifier, errorResponse.Error);
            }
        }

        // Change the trader status to Failed in the database.
        var result = await PersistFailedStatus();
        if (result is Result<bool>.Error errorResult)
        {
            Logger.LogError("Failed to mark trader {identifier} as failed in the main database. Reason: {reason}", TraderIdentifier, errorResult.ErrorValue);
        }

        Context.Parent.Tell(new ExistingTraderNotStartedResponse
        {
            TraderIdentifier = TraderIdentifier,
            Error = message.Error
        });

        Context.Stop(Self);
    }

    private async Task<Result<bool>> PersistFailedStatus()
    {
        try
        {
            await LifecycleService.MarkTraderFailed(TraderIdentifier.TraderGuid);

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new FailedDatabaseWriteError(e.ToString()));
        }
    }
}
