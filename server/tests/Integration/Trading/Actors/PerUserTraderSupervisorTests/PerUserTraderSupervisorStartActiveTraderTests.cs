using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;
using Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests.Fixtures;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests;

public class PerUserTraderSupervisorStartActiveTraderTests
{
    [Fact]
    public async Task StartActiveTrader_WhenStatusIsFailed_StartsTrader()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-illegal-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.GetCurrentStatus(traderGuid).Returns(Task.FromResult(TraderStatusEnum.Failed));
        lifecycle.GetTraderConfiguration(traderGuid).Returns(Task.FromResult(CreateConfig(userGuid)));
        lifecycle.MarkTraderRunning(traderGuid).Returns(Task.CompletedTask);

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        Assert.IsType<ExistingTraderStartedResponse>(response);
        await lifecycle.Received(1).MarkTraderRunning(traderGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartActiveTrader_WhenSameTraderIsAlreadyInProgress_ReturnsDuplicateStartError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-duplicate-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        supervisor.Tell(new StartActiveTraderRequest { TraderIdentifier = traderIdentifier });

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<ExistingTraderNotStartedResponse>(response);
        Assert.IsType<TraderStartUserError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartActiveTrader_WhenDifferentTaskActive_ReturnsNotStarted()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-different-task-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        var stopWorkflowGate = new TaskCompletionSource<TraderEntity>(TaskCreationOptions.RunContinuationsAsynchronously);
        lifecycle.LoadTrader(traderGuid).Returns(stopWorkflowGate.Task);

        var brokerClientSupervisor = actorSystem.ActorOf(Props.Create(() => new BrokerClientSupervisorStub()));
        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid, brokerClientSupervisor);
        await WaitForSupervisorStarted(supervisor, userGuid);

        // Start a stop task to mark the trader as having a different active task
        supervisor.Tell(new StopTraderRequest { TraderIdentifier = traderIdentifier });

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);
        stopWorkflowGate.TrySetException(new InvalidOperationException("Release stop workflow after the conflicting start is handled."));

        var notStarted = Assert.IsType<ExistingTraderNotStartedResponse>(response);
        Assert.IsType<TraderStartUserError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartActiveTrader_WhenStatusFetchFails_ReturnsNotStartedWithFailedStatusFetchError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-status-fetch-fail-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.GetCurrentStatus(traderGuid)
            .Returns(_ => Task.FromException<TraderStatusEnum>(new InvalidOperationException("status failed")));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<ExistingTraderNotStartedResponse>(response);
        Assert.IsType<FailedStatusFetchError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartActiveTrader_AddTaskFailure_ReturnsInternalError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-existing-addtask-fail-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var testFactory = new TestActorTaskStoreFactory();

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid, testFactory);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<ExistingTraderNotStartedResponse>(response);
        Assert.IsType<TraderStartInternalError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartActiveTrader_StartsActor_AndMarksTraderRunning()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.GetCurrentStatus(traderGuid).Returns(Task.FromResult(TraderStatusEnum.Created));
        lifecycle.GetTraderConfiguration(traderGuid).Returns(Task.FromResult(CreateConfig(userGuid)));
        lifecycle.MarkTraderRunning(traderGuid).Returns(Task.CompletedTask);

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        Assert.IsType<ExistingTraderStartedResponse>(response);

        var actorResponse = await supervisor.Ask<GetTraderActorResponse>(
            new GetTraderActorRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        Assert.IsType<Result<IActorRef>.Ok>(actorResponse.Result);
        await lifecycle.Received(1).MarkTraderRunning(traderGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartActiveTrader_WhenStatusIsRunning_AndActorAlreadyRunning_ReturnsSuccessWithoutPersisting()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-running-noop-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.GetCurrentStatus(traderGuid)
            .Returns(
                Task.FromResult(TraderStatusEnum.Created),
                Task.FromResult(TraderStatusEnum.Running)
            );
        lifecycle.GetTraderConfiguration(traderGuid).Returns(Task.FromResult(CreateConfig(userGuid)));
        lifecycle.MarkTraderRunning(traderGuid).Returns(Task.CompletedTask);

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var firstStart = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);
        Assert.IsType<ExistingTraderStartedResponse>(firstStart);

        var secondStart = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);
        Assert.IsType<ExistingTraderStartedResponse>(secondStart);

        // The first start should have persisted the Running status; the second start is a noop and should not persist again.
        await lifecycle.Received(1).MarkTraderRunning(traderGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartActiveTrader_WhenStatusIsRunning_AndActorMissing_RecreatesActorAndPersistsRunning()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-running-recover-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.GetCurrentStatus(traderGuid).Returns(Task.FromResult(TraderStatusEnum.Running));
        lifecycle.GetTraderConfiguration(traderGuid).Returns(Task.FromResult(CreateConfig(userGuid)));
        lifecycle.MarkTraderRunning(traderGuid).Returns(Task.CompletedTask);

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var startResponse = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        Assert.IsType<ExistingTraderStartedResponse>(startResponse);
        await lifecycle.Received(1).MarkTraderRunning(traderGuid);

        var actorResponse = await supervisor.Ask<GetTraderActorResponse>(
            new GetTraderActorRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        Assert.IsType<Result<IActorRef>.Ok>(actorResponse.Result);

        await actorSystem.Terminate();
    }

    private static TraderConfiguration CreateConfig(NonEmptyGuid userGuid)
    {
        return new TraderConfiguration
        {
            UserId = 1,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialBaseInvestment = new NonNegativeDecimalNumber(100m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(50m)
        };
    }

    private static TraderIdentifier CreateIdentifier(NonEmptyGuid userGuid, NonEmptyGuid traderGuid)
    {
        return new TraderIdentifier
        {
            UserGuid = userGuid,
            TraderGuid = traderGuid
        };
    }

    private static IActorRef CreateSupervisorActor(
        ActorSystem actorSystem,
        ITraderLifecycleService lifecycle,
        NonEmptyGuid userGuid,
        IActorRef? brokerClientSupervisorReference = null
    )
    {
        var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        dbConfigFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>())
            .Returns(Substitute.For<IDatabaseConfig>());

        var migrationService = Substitute.For<ITraderDbMigrationService>();
        migrationService.RunMigrations(Arg.Any<IDatabaseConfig>())
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(lifecycle);
        services.AddSingleton<ILogger<PerUserTraderSupervisor>>(_ => NullLogger<PerUserTraderSupervisor>.Instance);
        services.AddSingleton<IActorTaskStoreFactory>(_ => new ActorTaskStoreFactory());

        var serviceProvider = services.BuildServiceProvider();
        var lifeCycleService = serviceProvider.GetRequiredService<ITraderLifecycleService>();
        var actorTaskStoreFactory = serviceProvider.GetRequiredService<IActorTaskStoreFactory>();

        var actorFactory = new TraderActorFactory(dbConfigFactory, migrationService, NullLoggerFactory.Instance);
        var workflowFactory = new TraderWorkflowActorFactory(
            dbConfigFactory,
            migrationService,
            lifeCycleService,
            Substitute.For<IPositionRepositoryFactory>(),
            new TraderEntityToTraderConfigurationTransformer(),
            NullLoggerFactory.Instance
        );

        return actorSystem.ActorOf(Props.Create(() => new PerUserTraderSupervisor(
            lifeCycleService,
            actorFactory,
            workflowFactory,
            actorTaskStoreFactory,
            brokerClientSupervisorReference ?? ActorRefs.Nobody,
            NullLogger<PerUserTraderSupervisor>.Instance,
            userGuid
        )));
    }

    private static IActorRef CreateSupervisorActor(ActorSystem actorSystem, ITraderLifecycleService lifecycle, NonEmptyGuid userGuid, IActorTaskStoreFactory actorTaskStoreFactory)
    {
        var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        dbConfigFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>())
            .Returns(Substitute.For<IDatabaseConfig>());

        var migrationService = Substitute.For<ITraderDbMigrationService>();
        migrationService.RunMigrations(Arg.Any<IDatabaseConfig>())
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(lifecycle);
        services.AddSingleton<ILogger<PerUserTraderSupervisor>>(_ => NullLogger<PerUserTraderSupervisor>.Instance);
        services.AddSingleton<IActorTaskStoreFactory>(_ => actorTaskStoreFactory);

        var serviceProvider = services.BuildServiceProvider();
        var lifeCycleService = serviceProvider.GetRequiredService<ITraderLifecycleService>();

        var actorFactory = new TraderActorFactory(dbConfigFactory, migrationService, NullLoggerFactory.Instance);
        var workflowFactory = new TraderWorkflowActorFactory(
            dbConfigFactory,
            migrationService,
            lifeCycleService,
            Substitute.For<IPositionRepositoryFactory>(),
            new TraderEntityToTraderConfigurationTransformer(),
            NullLoggerFactory.Instance
        );

        return actorSystem.ActorOf(Props.Create(() => new PerUserTraderSupervisor(
            lifeCycleService,
            actorFactory,
            workflowFactory,
            actorTaskStoreFactory,
            ActorRefs.Nobody,
            NullLogger<PerUserTraderSupervisor>.Instance,
            userGuid
        )));
    }

    private static async Task WaitForSupervisorStarted(IActorRef supervisor, NonEmptyGuid userGuid)
    {
        await supervisor
            .Ask<GetTraderActorResponse>(
                new GetTraderActorRequest
                {
                    TraderIdentifier = CreateIdentifier(userGuid, NonEmptyGuid.NewGuid())
                },
                TestContext.Current.CancellationToken
            )
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private sealed class BrokerClientSupervisorStub : ReceiveActor
    {
        public BrokerClientSupervisorStub()
        {
            ReceiveAny(_ => { });
        }
    }
}
