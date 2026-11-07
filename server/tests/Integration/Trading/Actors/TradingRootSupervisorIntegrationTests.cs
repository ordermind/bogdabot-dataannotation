using Akka.Actor;
using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Repositories;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Transformers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetPerUserTraderSupervisorProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopAllTradersForUserProtocol;
using Akka.DependencyInjection;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors;

public class TradingRootSupervisorIntegrationTests
{
    private sealed class TradingInitializedObserver : ReceiveActor
    {
        public TradingInitializedObserver(TaskCompletionSource<bool> completionSource)
        {
            Receive<TradingInitializedEvent>(_ =>
            {
                completionSource.TrySetResult(true);
                Context.Stop(Self);
            });
        }
    }

    [Fact]
    public async Task OnBoot_StartsPerUserSupervisorAndRestoresRunningTraderActor()
    {
        var actorSystemName = $"root-supervisor-boot-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = new TraderIdentifier { UserGuid = userGuid, TraderGuid = traderGuid };

        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetAllUsersWithActiveTraders().Returns(Task.FromResult<IEnumerable<UserEntity>>([
            new UserEntity
            {
                Id = 1,
                Guid = userGuid,
                UserName = "tester",
                NormalizedUserName = "TESTER",
                Email = "tester@example.com",
                NormalizedEmail = "TESTER@EXAMPLE.COM"
            }
        ]));

        var lifecycleService = Substitute.For<ITraderLifecycleService>();
        lifecycleService.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([traderIdentifier]));
        lifecycleService.GetCurrentStatus(traderGuid).Returns(Task.FromResult(TraderStatusEnum.Running));
        lifecycleService.GetTraderConfiguration(traderGuid).Returns(Task.FromResult(CreateConfig(userGuid)));
        lifecycleService.MarkTraderRunning(traderGuid).Returns(Task.CompletedTask);

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        await WaitForRootSupervisorStarted(actorSystem, root, userGuid);

        var supervisorResponse = await root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        var userSupervisor = Assert.IsType<TraderSupervisorFoundResponse>(supervisorResponse).TraderSupervisorReference;

        await WaitUntil(async () =>
        {
            var response = await userSupervisor.Ask<GetTraderActorResponse>(
                new GetTraderActorRequest { TraderIdentifier = traderIdentifier },
                TestContext.Current.CancellationToken);

            return response.Result is Result<IActorRef>.Ok;
        });

        await lifecycleService.Received(1).MarkTraderRunning(traderGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task GetTraderSupervisorRequest_WhenCreateSupervisorFails_ReturnsNotFoundAndCleansUpPendingTask()
    {
        var actorSystemName = $"root-supervisor-create-fail-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();

        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetAllUsersWithActiveTraders().Returns(Task.FromResult<IEnumerable<UserEntity>>([]));

        var lifecycleService = Substitute.For<ITraderLifecycleService>();
        lifecycleService.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var failTraderSupervisorLoggerCreation = true;
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(call =>
        {
            if (failTraderSupervisorLoggerCreation
                && call.Arg<string>() == "Bogdabot.Server.Trading.Actors.PerUserTraderSupervisor")
            {
                failTraderSupervisorLoggerCreation = false;
                throw new InvalidOperationException("Simulated supervisor creation failure.");
            }

            return NullLogger.Instance;
        });

        var (actorFactory, workflowFactory, actorTaskStoreFactory, _, testServiceProvider) = CreateDependencies(lifecycleService, userRepository, loggerFactory);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        var response = await root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        Assert.IsType<TraderSupervisorNotFoundResponse>(response);

        var retryResponse = await root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        Assert.IsType<TraderSupervisorFoundResponse>(retryResponse);
        await actorSystem.Terminate();
    }

    [Fact]
    public async Task GetTraderSupervisorRequest_CreatesAndReturnsSupervisor_WhenMissing()
    {
        var actorSystemName = $"root-supervisor-get-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();

        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetAllUsersWithActiveTraders().Returns(Task.FromResult<IEnumerable<UserEntity>>([]));

        var lifecycleService = Substitute.For<ITraderLifecycleService>();
        lifecycleService.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        await WaitForRootSupervisorStarted(actorSystem, root, userGuid);

        var first = await root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        var second = await root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        var firstRef = Assert.IsType<TraderSupervisorFoundResponse>(first).TraderSupervisorReference;
        var secondRef = Assert.IsType<TraderSupervisorFoundResponse>(second).TraderSupervisorReference;

        Assert.Equal(firstRef.Path.ToStringWithoutAddress(), secondRef.Path.ToStringWithoutAddress());

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task GetTraderSupervisorRequest_RecreatesSupervisor_AfterPerUserSupervisorTerminates()
    {
        var actorSystemName = $"root-supervisor-recreate-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();

        var userRepository = Substitute.For<IUserRepository>();
        userRepository
            .GetAllUsersWithActiveTraders()
            .Returns(Task.FromResult<IEnumerable<UserEntity>>([]));

        var lifecycleService = Substitute.For<ITraderLifecycleService>();

        var activeCallCount = 0;

        lifecycleService
            .GetActiveTradersForUser(userGuid)
            .Returns(_ =>
            {
                Interlocked.Increment(ref activeCallCount);
                return Task.FromResult<IEnumerable<TraderIdentifier>>([]);
            });

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(
            Props.Create(() => new TradingRootSupervisor(
                resolver,
                actorTaskStoreFactory,
                userRepository,
                NullLogger<TradingRootSupervisor>.Instance
            )));

        await WaitForRootSupervisorStarted(actorSystem, root, userGuid);

        var firstResponse = await root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest
            {
                UserGuid = userGuid
            },
            TestContext.Current.CancellationToken);

        var firstSupervisor =
            Assert.IsType<TraderSupervisorFoundResponse>(firstResponse)
                .TraderSupervisorReference;

        // Wait until the first supervisor has performed its initialization
        // and called into the lifecycle service.
        await WaitUntil(() =>
            Task.FromResult(
                Volatile.Read(ref activeCallCount) >= 1));

        var stopped = await firstSupervisor.GracefulStop(
            TimeSpan.FromSeconds(2));

        Assert.True(stopped);

        await WaitUntil(async () =>
        {
            try
            {
                var secondResponse = await root
                    .Ask<GetTraderSupervisorResponse>(
                        new GetTraderSupervisorRequest
                        {
                            UserGuid = userGuid
                        },
                        TestContext.Current.CancellationToken)
                    .WaitAsync(
                        TimeSpan.FromMilliseconds(200),
                        TestContext.Current.CancellationToken);

                var secondSupervisor =
                    Assert.IsType<TraderSupervisorFoundResponse>(secondResponse)
                        .TraderSupervisorReference;

                await secondSupervisor
                    .Ask<GetTraderActorResponse>(
                        new GetTraderActorRequest
                        {
                            TraderIdentifier = new TraderIdentifier
                            {
                                UserGuid = userGuid,
                                TraderGuid = NonEmptyGuid.NewGuid()
                            }
                        },
                        TestContext.Current.CancellationToken)
                    .WaitAsync(
                        TimeSpan.FromMilliseconds(200),
                        TestContext.Current.CancellationToken);

                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        });

        // Ensure both the original and the recreated supervisor
        // executed initialization.
        await WaitUntil(() =>
            Task.FromResult(
                Volatile.Read(ref activeCallCount) >= 2));

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopAllTradersRequest_WhenNoSupervisorExists_ReturnsNotStopped()
    {
        var actorSystemName = $"root-supervisor-stop-none-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();

        var userRepository = Substitute.For<IUserRepository>();
        userRepository
            .GetAllUsersWithActiveTraders()
            .Returns(Task.FromResult<IEnumerable<UserEntity>>([]));

        var lifecycleService = Substitute.For<ITraderLifecycleService>();

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        var response = await root.Ask<StopAllTradersResponse>(
            new StopAllTradersRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        Assert.IsType<AllTradersNotStoppedResponse>(response);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopAllTradersRequest_WithSupervisorExists_ReturnsStopped()
    {
        var actorSystemName = $"root-supervisor-stop-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();

        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetAllUsersWithActiveTraders().Returns(Task.FromResult<IEnumerable<UserEntity>>([]));

        var lifecycleService = Substitute.For<ITraderLifecycleService>();
        lifecycleService.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        // Ensure the per-user supervisor exists
        await WaitForRootSupervisorStarted(actorSystem, root, userGuid);

        var response = await root.Ask<StopAllTradersResponse>(
            new StopAllTradersRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        Assert.IsType<AllTradersStoppedResponse>(response);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task OnBoot_WaitsForPerUserSupervisorInitializationBeforePublishingReadyEvent()
    {
        var actorSystemName = $"root-supervisor-readiness-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();
        var user = new UserEntity
        {
            Id = 1,
            Guid = userGuid,
            UserName = "tester",
            NormalizedUserName = "TESTER",
            Email = "tester@example.com",
            NormalizedEmail = "TESTER@EXAMPLE.COM"
        };

        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetAllUsersWithActiveTraders().Returns(Task.FromResult<IEnumerable<UserEntity>>([user]));

        var activeTradersTcs = new TaskCompletionSource<IEnumerable<TraderIdentifier>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycleService = Substitute.For<ITraderLifecycleService>();
        lifecycleService.GetActiveTradersForUser(userGuid).Returns(activeTradersTcs.Task);

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = actorSystem.ActorOf(Props.Create(() => new TradingInitializedObserver(completionSource)));
        actorSystem.EventStream.Subscribe(observer, typeof(TradingInitializedEvent));

        try
        {
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await completionSource.Task.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));

            activeTradersTcs.TrySetResult([]);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await completionSource.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            actorSystem.EventStream.Unsubscribe(observer, typeof(TradingInitializedEvent));
            actorSystem.Stop(observer);
        }

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task GetTraderSupervisorRequest_DuringInitialization_IsStashedUntilInitializationFinishes()
    {
        var actorSystemName = $"root-supervisor-stash-tests-{Guid.NewGuid()}";

        var requestedUserGuid = NonEmptyGuid.NewGuid();

        var usersLoadTcs = new TaskCompletionSource<IEnumerable<UserEntity>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetAllUsersWithActiveTraders().Returns(usersLoadTcs.Task);

        var lifecycleService = Substitute.For<ITraderLifecycleService>();
        lifecycleService.GetActiveTradersForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        var pendingResponse = root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest { UserGuid = requestedUserGuid },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await pendingResponse.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));

        usersLoadTcs.TrySetResult([]);

        var response = await pendingResponse;
        Assert.IsType<TraderSupervisorFoundResponse>(response);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task OnBoot_WhenFetchingUsersFailsOnce_RootRecoversAndServesRequests()
    {
        var actorSystemName = $"root-supervisor-users-fail-recover-tests-{Guid.NewGuid()}";

        var userGuid = NonEmptyGuid.NewGuid();

        var userRepository = Substitute.For<IUserRepository>();
        var fetchAttempts = 0;
        userRepository.GetAllUsersWithActiveTraders().Returns(_ =>
        {
            fetchAttempts++;

            return fetchAttempts == 1
                ? Task.FromException<IEnumerable<UserEntity>>(new InvalidOperationException("users read failed"))
                : Task.FromResult<IEnumerable<UserEntity>>([]);
        });

        var lifecycleService = Substitute.For<ITraderLifecycleService>();
        lifecycleService.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>([]));

        var (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, testServiceProvider) = CreateDependencies(lifecycleService, userRepository);
        using var serviceProvider = testServiceProvider;
        using var actorSystem = ActorSystem.Create(actorSystemName, BootstrapSetup.Create().And(DependencyResolverSetup.Create(serviceProvider)));
        var resolver = DependencyResolver.For(actorSystem);

        var root = actorSystem.ActorOf(Props.Create(() => new TradingRootSupervisor(
            resolver,
            actorTaskStoreFactory,
            userRepository,
            NullLogger<TradingRootSupervisor>.Instance
        )));

        await WaitForRootSupervisorStarted(actorSystem, root, userGuid);

        var response = await root.Ask<GetTraderSupervisorResponse>(
            new GetTraderSupervisorRequest { UserGuid = userGuid },
            TestContext.Current.CancellationToken);

        Assert.IsType<TraderSupervisorFoundResponse>(response);
        await userRepository.Received(1).GetAllUsersWithActiveTraders();

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

    private static (TraderActorFactory actorFactory, TraderWorkflowActorFactory workflowFactory, IActorTaskStoreFactory actorTaskStoreFactory, ILoggerFactory loggerFactory, Microsoft.Extensions.DependencyInjection.ServiceProvider serviceProvider) CreateDependencies(
        ITraderLifecycleService lifecycleService,
        IUserRepository userRepository,
        ILoggerFactory? loggerFactory = null
    )
    {
        var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        dbConfigFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>())
            .Returns(Substitute.For<IDatabaseConfig>());

        var migrationService = Substitute.For<ITraderDbMigrationService>();
        migrationService.RunMigrations(Arg.Any<IDatabaseConfig>())
            .Returns(Task.CompletedTask);

        var actorTaskStoreFactory = new IntegrationActorTaskStoreFactory();
        var actorFactory = new TraderActorFactory(dbConfigFactory, migrationService, NullLoggerFactory.Instance);
        var workflowFactory = new TraderWorkflowActorFactory(
            dbConfigFactory,
            migrationService,
            lifecycleService,
            Substitute.For<IPositionRepositoryFactory>(),
            new TraderEntityToTraderConfigurationTransformer(),
            NullLoggerFactory.Instance
        );
        loggerFactory ??= NullLoggerFactory.Instance;

        var services = new ServiceCollection();
        services.AddSingleton(lifecycleService);
        services.AddSingleton(userRepository);
        services.AddSingleton<IActorTaskStoreFactory>(actorTaskStoreFactory);
        services.AddSingleton(actorFactory);
        services.AddSingleton(workflowFactory);
        services.AddSingleton(loggerFactory);
        services.AddSingleton<ILogger<PerUserTraderSupervisor>>(provider => provider.GetRequiredService<ILoggerFactory>().CreateLogger<PerUserTraderSupervisor>());
        services.AddSingleton<ILogger<PerUserBrokerClientSupervisor>>(provider => provider.GetRequiredService<ILoggerFactory>().CreateLogger<PerUserBrokerClientSupervisor>());

        var serviceProvider = services.BuildServiceProvider();

        return (actorFactory, workflowFactory, actorTaskStoreFactory, loggerFactory, serviceProvider);
    }

    // Integration-friendly ActorTaskStoreFactory that mirrors production behaviour.
    private class IntegrationActorTaskStoreFactory : IActorTaskStoreFactory
    {
        public ActorTaskStore<TTask> Create<TTask>() where TTask : notnull
        {
            return new ActorTaskStore<TTask>();
        }
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        try
        {
            await Poll().WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                "Condition was not met within the expected timeout.");
        }

        async Task Poll()
        {
            while (true)
            {
                if (await condition())
                {
                    return;
                }

                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
        }
    }

    private static async Task WaitForRootSupervisorStarted(ActorSystem actorSystem, IActorRef root, NonEmptyGuid userGuid)
    {
        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = actorSystem.ActorOf(Props.Create(() => new TradingInitializedObserver(completionSource)));
        actorSystem.EventStream.Subscribe(observer, typeof(TradingInitializedEvent));

        try
        {
            // Root can publish TradingInitializedEvent before this helper subscribes.
            // If that happens, the event wait times out even though the root is already ready.
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await completionSource.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Fall through to the bounded readiness probe below.
            }

            var response = await root
                .Ask<GetTraderSupervisorResponse>(
                    new GetTraderSupervisorRequest { UserGuid = userGuid },
                    TestContext.Current.CancellationToken
                )
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.IsType<TraderSupervisorFoundResponse>(response);
        }
        finally
        {
            actorSystem.EventStream.Unsubscribe(observer, typeof(TradingInitializedEvent));
            actorSystem.Stop(observer);
        }
    }
}
