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
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.DeleteTraderProtocol;
using Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests.Fixtures;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests;

public class PerUserTraderSupervisorDeleteTraderTests
{
    [Fact]
    public async Task DeleteTrader_AddTaskFailure_ReturnsInternalError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-delete-addtask-fail-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var testFactory = new TestActorTaskStoreFactory();

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid, testFactory);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<TraderNotDeletedResponse>(
            new DeleteTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        var notDeleted = Assert.IsType<TraderNotDeletedResponse>(response);
        Assert.IsType<TraderDeleteInternalError>(notDeleted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task DeleteTrader_DeletesMainEntry_AndTraderDatabase()
    {
        using var actorSystem = ActorSystem.Create($"per-user-delete-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.DeleteTraderByGuid(traderGuid).Returns(Task.CompletedTask);

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<DeleteTraderResponse>(
            new DeleteTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        Assert.IsType<TraderDeletedResponse>(response);
        await lifecycle.Received(1).DeleteTraderByGuid(traderGuid);
        var deleteDbCalls = lifecycle.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(Bogdabot.Server.Trading.StateManagement.TraderLifecycleService.DeleteTraderDatabase)).ToArray();
        Assert.True(deleteDbCalls.Length >= 1, $"Expected at least one call to DeleteTraderDatabase, got {deleteDbCalls.Length}");

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task DeleteTrader_WhenDeletingTraderDatabaseFails_ReturnsNotDeleted()
    {
        using var actorSystem = ActorSystem.Create($"per-user-delete-db-cleanup-fail-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = CreateIdentifier(userGuid, traderGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.DeleteTraderByGuid(traderGuid).Returns(Task.CompletedTask);
        lifecycle.When(x => x.DeleteTraderDatabase(Arg.Any<IDatabaseConfig>()))
            .Do(_ => throw new InvalidOperationException("db file cleanup failed"));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<DeleteTraderResponse>(
            new DeleteTraderRequest { TraderIdentifier = traderIdentifier },
            TestContext.Current.CancellationToken);

        var notDeleted = Assert.IsType<TraderNotDeletedResponse>(response);
        Assert.IsType<FailedDatabaseCleanupError>(notDeleted.Error);

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

    private static IActorRef CreateSupervisorActor(ActorSystem actorSystem, ITraderLifecycleService lifecycle, NonEmptyGuid userGuid)
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
            ActorRefs.Nobody,
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
            .Ask<DeleteTraderResponse>(
                new DeleteTraderRequest
                {
                    TraderIdentifier = CreateIdentifier(userGuid, NonEmptyGuid.NewGuid())
                },
                TestContext.Current.CancellationToken
            )
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
