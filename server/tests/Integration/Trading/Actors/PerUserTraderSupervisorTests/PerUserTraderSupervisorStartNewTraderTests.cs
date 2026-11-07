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
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopAllTradersForUserProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.DeleteTraderProtocol;
using Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests.Fixtures;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests;

public class PerUserTraderSupervisorStartNewTraderTests
{
    [Fact]
    public async Task StartNewTrader_AddTaskFailure_ReturnsInternalError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-new-addtask-fail-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var config = CreateConfig(userGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.CreateTrader(Arg.Any<TraderConfiguration>(), Arg.Any<NonNegativeDecimalNumber>()).Returns(Task.FromResult(traderGuid));

        var testFactory = new TestActorTaskStoreFactory();

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid, testFactory);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartTraderResponse>(
            new StartNewTraderRequest { Config = config },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<ExistingTraderNotStartedResponse>(response);
        Assert.IsType<TraderStartInternalError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartNewTrader_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-new-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartTraderResponse>(
            new StartNewTraderRequest { Config = CreateConfig(wrongUserGuid) },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<NewTraderNotStartedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartExistingTrader_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-existing-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new StartActiveTraderRequest
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                }
            },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<ExistingTraderNotStartedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartTraderActor_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-trader-actor-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var config = CreateConfig(userGuid);

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartTraderActorResponse>(
            new StartTraderActorRequest
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                },
                Config = config
            },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<TraderActorNotStartedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartExistingTraderResponse_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-existing-response-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartExistingTraderResponse>(
            new ExistingTraderNotStartedResponse
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                },
                Error = new InternalError("wrong supervisor")
            },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<ExistingTraderNotStartedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartTraderActorResponse_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-trader-actor-response-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartTraderActorResponse>(
            new TraderActorNotStartedResponse
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                },
                Config = CreateConfig(userGuid),
                Error = new InternalError("wrong supervisor")
            },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<TraderActorNotStartedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopAllTraders_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-stop-all-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StopAllTradersResponse>(
            new StopAllTradersRequest { UserGuid = wrongUserGuid },
            TestContext.Current.CancellationToken);

        var notStopped = Assert.IsType<AllTradersNotStoppedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStopped.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopTrader_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-stop-trader-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StopTraderResponse>(
            new StopTraderRequest
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                }
            },
            TestContext.Current.CancellationToken);

        var notStopped = Assert.IsType<TraderNotStoppedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStopped.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopTraderActor_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-stop-trader-actor-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StopTraderActorResponse>(
            new StopTraderActorRequest
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                }
            },
            TestContext.Current.CancellationToken);

        var notStopped = Assert.IsType<TraderActorNotStoppedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStopped.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopTraderActorResponse_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-stop-trader-actor-response-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StopTraderActorResponse>(
            new TraderActorNotStoppedResponse
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                },
                Error = new InternalError("wrong supervisor")
            },
            TestContext.Current.CancellationToken);

        var notStopped = Assert.IsType<TraderActorNotStoppedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStopped.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopTraderResponse_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-stop-trader-response-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StopTraderResponse>(
            new TraderNotStoppedResponse
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                },
                Error = new InternalError("wrong supervisor")
            },
            TestContext.Current.CancellationToken);

        var notStopped = Assert.IsType<TraderNotStoppedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notStopped.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task DeleteTrader_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-delete-trader-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<DeleteTraderResponse>(
            new DeleteTraderRequest
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                }
            },
            TestContext.Current.CancellationToken);

        var notDeleted = Assert.IsType<TraderNotDeletedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notDeleted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task DeleteTraderResponse_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-delete-trader-response-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<DeleteTraderResponse>(
            new TraderNotDeletedResponse
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                },
                Error = new InternalError("wrong supervisor")
            },
            TestContext.Current.CancellationToken);

        var notDeleted = Assert.IsType<TraderNotDeletedResponse>(response);
        Assert.IsType<IllegalActorMessageError>(notDeleted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task GetTraderActor_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"per-user-get-trader-actor-wrong-user-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<GetTraderActorResponse>(
            new GetTraderActorRequest
            {
                TraderIdentifier = new TraderIdentifier
                {
                    UserGuid = wrongUserGuid,
                    TraderGuid = traderGuid
                }
            },
            TestContext.Current.CancellationToken);

        var error = Assert.IsType<Result<IActorRef>.Error>(response.Result);
        Assert.IsType<IllegalActorMessageError>(error.ErrorValue);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task
    StartNewTrader_WhenCreateMainDbEntryFails_ReturnsNotStarted()
    {
        using var actorSystem = ActorSystem.Create($"per-user-start-new-create-fail-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));
        lifecycle.CreateTrader(Arg.Any<TraderConfiguration>(), Arg.Any<NonNegativeDecimalNumber>())
            .Returns(_ => Task.FromException<NonEmptyGuid>(new InvalidOperationException("create failed")));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);
        await WaitForSupervisorStarted(supervisor, userGuid);

        var response = await supervisor.Ask<StartTraderResponse>(
            new StartNewTraderRequest { Config = CreateConfig(userGuid) },
            TestContext.Current.CancellationToken);

        var notStarted = Assert.IsType<NewTraderNotStartedResponse>(response);
        Assert.IsType<FailedDatabaseWriteError>(notStarted.Error);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartNewTraderRequest_DuringInitialization_IsStashedUntilInitializationFinishes()
    {
        using var actorSystem = ActorSystem.Create($"per-user-initialization-stash-tests-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var config = CreateConfig(userGuid);

        var activeTradersLoadTcs = new TaskCompletionSource<IEnumerable<TraderIdentifier>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetActiveTradersForUser(userGuid).Returns(activeTradersLoadTcs.Task);
        lifecycle.CreateTrader(Arg.Any<TraderConfiguration>(), Arg.Any<NonNegativeDecimalNumber>())
            .Returns(_ => Task.FromException<NonEmptyGuid>(new InvalidOperationException("create failed")));

        var supervisor = CreateSupervisorActor(actorSystem, lifecycle, userGuid);

        var pendingResponse = supervisor.Ask<StartTraderResponse>(
            new StartNewTraderRequest { Config = config },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await pendingResponse.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));

        activeTradersLoadTcs.TrySetResult([]);

        var response = await pendingResponse;
        Assert.IsType<NewTraderNotStartedResponse>(response);
        await lifecycle.Received(1).CreateTrader(Arg.Any<TraderConfiguration>(), Arg.Any<NonNegativeDecimalNumber>());

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
            .Ask<GetTraderActorResponse>(
                new GetTraderActorRequest
                {
                    TraderIdentifier = CreateIdentifier(userGuid, NonEmptyGuid.NewGuid())
                },
                TestContext.Current.CancellationToken
            )
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
