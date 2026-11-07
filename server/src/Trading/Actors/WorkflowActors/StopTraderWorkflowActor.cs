using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.Trading.DTO;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.GetCurrentPriceProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetBrokerClientProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Trading.Actors.WorkflowActors;

public sealed class StopTraderWorkflowActor(
    ITraderLifecycleService lifecycleService,
    ITraderDatabaseConfigFactory traderDbConfigFactory,
    IPositionRepositoryFactory positionRepositoryFactory,
    TraderEntityToTraderConfigurationTransformer traderConfigTransformer,
    ILogger<StopTraderWorkflowActor> logger,
    TraderIdentifier traderIdentifier,
    IActorRef brokerClientSupervisorReference
) : ReceiveActor
{
    private ITraderLifecycleService LifecycleService { get; } = lifecycleService;
    private ITraderDatabaseConfigFactory TraderDbConfigFactory { get; } = traderDbConfigFactory;
    private IPositionRepositoryFactory PositionRepositoryFactory { get; } = positionRepositoryFactory;
    private TraderEntityToTraderConfigurationTransformer TraderConfigTransformer { get; } = traderConfigTransformer;
    private ILogger<StopTraderWorkflowActor> Logger { get; } = logger;
    private TraderIdentifier TraderIdentifier { get; } = traderIdentifier;
    private IActorRef BrokerClientSupervisorReference { get; } = brokerClientSupervisorReference;
    private TraderConfiguration? TraderConfiguration { get; set; } = null;
    private TraderStatusEnum? OldStatus { get; set; } = null;

    private static readonly TraderStatusEnum[] StoppableStatuses =
    [
        TraderStatusEnum.Running,
        TraderStatusEnum.Failed,
    ];

    #region External messages
    public record StartWorkflowActor { }
    #endregion

    #region Internal messages
    private record FetchTraderPropertiesInternalMessage { }
    private record ValidateStatusChangeInternalMessage { }
    private record StopActorInternalMessage { }
    private record FetchTraderSummaryInternalMessage { }
    private record FetchTraderDataInternalMessage { }
    private record PersistStoppedTraderInternalMessage
    {
        public required NonNegativeDecimalNumber FinalPrice { get; init; }
        public required NonNegativeDecimalNumber FinalBaseBalance { get; init; }
        public required NonNegativeDecimalNumber FinalQuoteBalance { get; init; }
        public required decimal RealizedResult { get; init; }
    }
    private record CleanupDatabasesInternalMessage { }
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
        Become(PhaseFetchingTraderProperties);

        Self.Tell(new FetchTraderPropertiesInternalMessage { });
    }

    private void PhaseFetchingTraderProperties()
    {
        ReceiveAsync<FetchTraderPropertiesInternalMessage>(HandleFetchTraderPropertiesStep);
    }

    private async Task HandleFetchTraderPropertiesStep(FetchTraderPropertiesInternalMessage _)
    {
        var result = await FetchTrader();

        if (result is Result<TraderEntity>.Error error)
        {
            Logger.LogWarning("Error fetching the trader, possibly because the trader has already been stopped. Identifier: {identifier}. Reason: {reason}", TraderIdentifier, error.ErrorValue);

            if (await IsTraderActorStopped())
            {
                Logger.LogWarning("The trader actor {identifier} is not running, I will do a noop", TraderIdentifier);

                Context.Parent.Tell(new TraderStoppedResponse
                {
                    TraderIdentifier = TraderIdentifier
                });

                Context.Stop(Self);

                return;
            }

            Logger.LogWarning("Trader {identifier} does not exist in the active traders table but the actor is still running. Stopping trader actor.", TraderIdentifier);

            Become(PhaseStoppingActor);

            Self.Tell(new StopActorInternalMessage());
        }
        else
        {
            var ok = (Result<TraderEntity>.Ok)result;
            var trader = ok.Value;

            OldStatus = trader.Status;
            TraderConfiguration = TraderConfigTransformer.transformActiveTrader(trader);

            Become(PhaseValidatingStatusChange);

            Self.Tell(new ValidateStatusChangeInternalMessage { });
        }
    }

    private async Task<Result<TraderEntity>> FetchTrader()
    {
        try
        {
            var trader = await LifecycleService.LoadTrader(TraderIdentifier.TraderGuid);

            return new Result<TraderEntity>.Ok(trader);
        }
        catch (Exception e)
        {
            return new Result<TraderEntity>.Error(new FailedStatusFetchError(e.ToString()));
        }
    }

    private void PhaseValidatingStatusChange()
    {
        ReceiveAsync<ValidateStatusChangeInternalMessage>(HandleValidateStatusChangeStep);
    }

    private async Task HandleValidateStatusChangeStep(ValidateStatusChangeInternalMessage _)
    {
        var oldStatus = OldStatus ?? throw new InvalidOperationException("OldStatus must have been set before validating the status");

        if (!StoppableStatuses.Contains(oldStatus))
        {
            Context.Parent.Tell(new TraderNotStoppedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = new IllegalStatusTransitionError($"You cannot stop a trader with the status {oldStatus}")
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseStoppingActor);

        Self.Tell(new StopActorInternalMessage());
    }

    private async Task<bool> IsTraderActorStopped()
    {
        var response = await Context.Parent.Ask<GetTraderActorResponse>(
            new GetTraderActorRequest { TraderIdentifier = TraderIdentifier }
        );

        return response.Result is Result<IActorRef>.Error;
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
            Logger.LogError("Failed to stop trader {identifier}. Reason: {reason}", TraderIdentifier, errorResponse.Error);

            Context.Parent.Tell(new TraderNotStoppedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = errorResponse.Error
            });

            Context.Stop(Self);

            return;
        }

        if (OldStatus is null)
        {
            // OldStatus being null here means that the status of the trader could not be found, most likely because it was already stopped. However, the trader actor was for some reason still running and has now been stopped. Skip the persisting step and go right to cleaning up databases.

            Become(PhaseCleanupDatabases);

            Self.Tell(new CleanupDatabasesInternalMessage { });
        }
        else
        {
            Become(PhaseFetchingTraderSummary);

            Self.Tell(new FetchTraderDataInternalMessage { });
        }
    }

    private void PhaseFetchingTraderSummary()
    {
        ReceiveAsync<FetchTraderDataInternalMessage>(HandleFetchTraderDataStep);
    }

    private async Task HandleFetchTraderDataStep(FetchTraderDataInternalMessage message)
    {
        var traderConfig = TraderConfiguration ?? throw new InvalidOperationException("TraderConfiguration must have been set before fetching the trader data");

        var currentPriceTask = FetchCurrentPrice(traderConfig);
        var positionSummaryTask = FetchPositionSummary(traderConfig);

        await Task.WhenAll(currentPriceTask, positionSummaryTask);

        var currentPriceResult = currentPriceTask.Result;
        var positionSummaryResult = positionSummaryTask.Result;

        if (currentPriceResult is Result<NonNegativeDecimalNumber>.Error currentPriceError)
        {
            Logger.LogError(
                "Trader {identifier} reported stopped, but the current price for the stopped trader could not be fetched: {message}",
                TraderIdentifier,
                currentPriceError.ErrorValue
            );

            Context.Parent.Tell(new TraderNotStoppedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = currentPriceError.ErrorValue
            });

            Context.Stop(Self);

            return;
        }

        var currentPriceOk = (Result<NonNegativeDecimalNumber>.Ok)currentPriceResult;
        var currentPrice = currentPriceOk.Value;

        if (positionSummaryResult is Result<PositionSummary>.Error positionSummaryError)
        {
            Logger.LogError(
                "Trader {identifier} reported stopped, but the summary for the stopped trader could not be fetched: {message}",
                TraderIdentifier,
                positionSummaryError.ErrorValue
            );

            Context.Parent.Tell(new TraderNotStoppedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = positionSummaryError.ErrorValue
            });

            Context.Stop(Self);

            return;
        }

        var positionSummaryOk = (Result<PositionSummary>.Ok)positionSummaryResult;
        var positionSummary = positionSummaryOk.Value;

        Become(PhasePersistingStoppedTrader);

        Self.Tell(new PersistStoppedTraderInternalMessage
        {
            FinalPrice = currentPrice,
            FinalBaseBalance = positionSummary.CurrentBaseBalance,
            FinalQuoteBalance = positionSummary.CurrentQuoteBalance,
            RealizedResult = positionSummary.RealizedResult,
        });
    }

    private async Task<Result<NonNegativeDecimalNumber>> FetchCurrentPrice(TraderConfiguration traderConfig)
    {
        if (BrokerClientSupervisorReference.IsNobody())
        {
            return new Result<NonNegativeDecimalNumber>.Error(new ActorNotFoundError($"The broker client supervisor for trader {TraderIdentifier} could not be found."));
        }

        var getBrokerClientRequest = new GetBrokerClientActorRequest
        {
            Identifier = new BrokerClientIdentifier
            {
                UserGuid = TraderIdentifier.UserGuid,
                BrokerName = traderConfig.BrokerName
            }
        };

        // TODO: Add timeout
        var getBrokerClientResponse = await BrokerClientSupervisorReference.Ask<GetBrokerClientActorResponse>(getBrokerClientRequest);

        var getBrokerClientResult = getBrokerClientResponse.Result;
        if (getBrokerClientResult is Result<IActorRef>.Error getBrokerClientError)
        {
            return new Result<NonNegativeDecimalNumber>.Error(new ActorNotFoundError($"The broker client actor for trader {TraderIdentifier} could not be found: {getBrokerClientError.ErrorValue}"));
        }

        var getBrokerClientOk = (Result<IActorRef>.Ok)getBrokerClientResult;

        var brokerClientActor = getBrokerClientOk.Value;

        var currentPriceRequest = new GetCurrentPriceRequest
        {
            BaseCurrency = traderConfig.BaseCurrency
        };

        // TODO: Add timeout
        var currentPriceResponse = await brokerClientActor.Ask<GetCurrentPriceResponse>(currentPriceRequest);

        return currentPriceResponse.Result;
    }

    private async Task<Result<PositionSummary>> FetchPositionSummary(TraderConfiguration traderConfig)
    {
        try
        {
            var repository = PositionRepositoryFactory.Create(TraderIdentifier.UserGuid, TraderActorId.FromTraderGuid(TraderIdentifier.TraderGuid));

            var positionSummary = await repository.GetSummary(traderConfig.InitialQuoteInvestment);

            return new Result<PositionSummary>.Ok(positionSummary);
        }
        catch (Exception e)
        {
            return new Result<PositionSummary>.Error(new FailedFetchingTraderSummaryError(e.ToString()));
        }
    }

    private void PhasePersistingStoppedTrader()
    {
        ReceiveAsync<PersistStoppedTraderInternalMessage>(HandlePersistStoppedTraderStep);
    }

    private async Task HandlePersistStoppedTraderStep(PersistStoppedTraderInternalMessage message)
    {
        var result = await PersistStoppedTrader(
            finalPrice: message.FinalPrice,
            finalBaseBalance: message.FinalBaseBalance,
            finalQuoteBalance: message.FinalQuoteBalance,
            realizedResult: message.RealizedResult
        );

        if (result is Result<bool>.Error error)
        {
            Logger.LogError(
                "Trader {identifier} reported stopped, but the stopped trader could not be persisted: {message}",
                TraderIdentifier,
                error.ErrorValue
            );

            Context.Parent.Tell(new TraderNotStoppedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Error = error.ErrorValue
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseCleanupDatabases);

        Self.Tell(new CleanupDatabasesInternalMessage { });
    }

    private async Task<Result<bool>> PersistStoppedTrader(
        NonNegativeDecimalNumber finalPrice,
        NonNegativeDecimalNumber finalBaseBalance,
        NonNegativeDecimalNumber finalQuoteBalance,
        decimal realizedResult
    )
    {
        try
        {
            await LifecycleService.PersistStoppedTrader(
                traderGuid: TraderIdentifier.TraderGuid,
                finalPrice: finalPrice,
                finalBaseBalance: finalBaseBalance,
                finalQuoteBalance: finalQuoteBalance,
                realizedResult: realizedResult
            );

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new FailedPersistingStoppedTraderError(e.ToString()));
        }
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
            Logger.LogError("Failed to clean up databases during stopping trader {identifier}. Reason: {reason}", TraderIdentifier, error.ErrorValue);

            Context.Parent.Tell(new TraderNotStoppedResponse
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
        Logger.LogDebug("The trader {identifier} was successfully stopped.", TraderIdentifier);

        Context.Parent.Tell(new TraderStoppedResponse
        {
            TraderIdentifier = TraderIdentifier
        });

        Context.Stop(Self);
    }
}
