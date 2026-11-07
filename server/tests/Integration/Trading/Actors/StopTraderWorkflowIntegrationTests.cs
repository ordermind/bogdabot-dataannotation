using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.Trading.DTO;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Actors.WorkflowActors;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Repositories;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.GetCurrentPriceProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetBrokerClientProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors;

public class StopTraderWorkflowIntegrationTests
{
    [Fact]
    public async Task StopWorkflow_PersistsFinalPriceAndPositionSummary_ThenCleansUp()
    {
        using var actorSystem = ActorSystem.Create($"stop-workflow-tests-{Guid.NewGuid()}");
        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = new TraderIdentifier { UserGuid = userGuid, TraderGuid = traderGuid };
        var initialQuoteBalance = new NonNegativeDecimalNumber(500m);
        var finalPrice = new NonNegativeDecimalNumber(125m);
        var finalBaseBalance = new NonNegativeDecimalNumber(2m);
        var finalQuoteBalance = new NonNegativeDecimalNumber(220m);
        var realizedResult = new NonNegativeDecimalNumber(30m);
        var trader = CreateTrader(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.LoadTrader(traderGuid).Returns(Task.FromResult(trader));
        lifecycle.PersistStoppedTrader(
                traderGuid,
                finalPrice,
                finalBaseBalance,
                finalQuoteBalance,
                realizedResult)
            .Returns(Task.CompletedTask);
        lifecycle.DeleteTraderByGuid(traderGuid).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(lifecycle);
        using var serviceProvider = services.BuildServiceProvider();

        var dbConfig = Substitute.For<IDatabaseConfig>();
        var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        dbConfigFactory.Create(userGuid, TraderActorId.FromTraderGuid(traderGuid)).Returns(dbConfig);

        var positionRepository = Substitute.For<IPositionRepository>();
        positionRepository.GetSummary(initialQuoteBalance).Returns(Task.FromResult(new PositionSummary
        {
            CurrentBaseBalance = finalBaseBalance,
            CurrentQuoteBalance = finalQuoteBalance,
            RealizedResult = realizedResult
        }));
        var positionRepositoryFactory = Substitute.For<IPositionRepositoryFactory>();
        positionRepositoryFactory.Create(userGuid, TraderActorId.FromTraderGuid(traderGuid)).Returns(positionRepository);

        var priceActor = actorSystem.ActorOf(Props.Create(() => new CurrentPriceActor(finalPrice)));
        var brokerSupervisor = actorSystem.ActorOf(Props.Create(() => new BrokerClientSupervisorActor(priceActor)));
        var stopped = new TaskCompletionSource<TraderStoppedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifeCycleService = serviceProvider.GetRequiredService<ITraderLifecycleService>();
        var parent = actorSystem.ActorOf(Props.Create(() => new StopWorkflowParent(
            lifeCycleService,
            dbConfigFactory,
            positionRepositoryFactory,
            traderIdentifier,
            brokerSupervisor,
            stopped)));

        parent.Tell(new BeginStopWorkflow());
        var response = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(traderIdentifier, response.TraderIdentifier);
        await lifecycle.Received(1).PersistStoppedTrader(
            traderGuid,
            finalPrice,
            finalBaseBalance,
            finalQuoteBalance,
            realizedResult);
        await lifecycle.Received(1).DeleteTraderByGuid(traderGuid);
        lifecycle.Received(1).DeleteTraderDatabase(dbConfig);
        _ = positionRepositoryFactory.Received(1).Create(userGuid, TraderActorId.FromTraderGuid(traderGuid));
        await positionRepository.Received(1).GetSummary(initialQuoteBalance);

        await actorSystem.Terminate();
    }

    private static TraderEntity CreateTrader(NonEmptyGuid userGuid, NonEmptyGuid traderGuid)
    {
        var now = DateTimeOffset.UtcNow;
        return new TraderEntity
        {
            Guid = traderGuid,
            UserId = 7,
            UserGuid = userGuid,
            Status = TraderStatusEnum.Running,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(1m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(500m),
            InitialTotalValue = new NonNegativeDecimalNumber(600m),
            FirstStartedAt = now,
            LastStartedAt = now
        };
    }

    private sealed record BeginStopWorkflow;

    private sealed class CurrentPriceActor : ReceiveActor
    {
        public CurrentPriceActor(NonNegativeDecimalNumber currentPrice)
        {
            Receive<GetCurrentPriceRequest>(request => Sender.Tell(new GetCurrentPriceResponse
            {
                BaseCurrency = request.BaseCurrency,
                Result = new Result<NonNegativeDecimalNumber>.Ok(currentPrice)
            }));
        }
    }

    private sealed class BrokerClientSupervisorActor : ReceiveActor
    {
        public BrokerClientSupervisorActor(IActorRef priceActor)
        {
            Receive<GetBrokerClientActorRequest>(_ => Sender.Tell(new GetBrokerClientActorResponse
            {
                Result = new Result<IActorRef>.Ok(priceActor)
            }));
        }
    }

    private sealed class StopWorkflowParent : ReceiveActor
    {
        public StopWorkflowParent(
            ITraderLifecycleService lifeCycleService,
            ITraderDatabaseConfigFactory databaseConfigFactory,
            IPositionRepositoryFactory positionRepositoryFactory,
            TraderIdentifier traderIdentifier,
            IActorRef brokerClientSupervisor,
            TaskCompletionSource<TraderStoppedResponse> stopped)
        {
            Receive<BeginStopWorkflow>(_ =>
            {
                var workflow = Context.ActorOf(Props.Create(() => new StopTraderWorkflowActor(
                    lifeCycleService,
                    databaseConfigFactory,
                    positionRepositoryFactory,
                    new TraderEntityToTraderConfigurationTransformer(),
                    NullLogger<StopTraderWorkflowActor>.Instance,
                    traderIdentifier,
                    brokerClientSupervisor)));
                workflow.Tell(new StopTraderWorkflowActor.StartWorkflowActor());
            });
            Receive<StopTraderActorRequest>(request => Sender.Tell(new TraderActorStoppedResponse
            {
                TraderIdentifier = request.TraderIdentifier
            }));
            Receive<TraderStoppedResponse>(response => stopped.TrySetResult(response));
            Receive<TraderNotStoppedResponse>(response => stopped.TrySetException(new InvalidOperationException(response.Error.ToString())));
        }
    }
}
