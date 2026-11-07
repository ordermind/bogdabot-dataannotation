using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.WorkflowActors;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.DeleteTraderProtocol;

namespace Bogdabot.Server.Tests.Unit.Trading.Actors;

public class WorkflowActorsTests
{
    [Fact]
    public async Task StartExistingTraderWorkflow_SkipsPersistWhenOldStatusIsRunning()
    {
        using var actorSystem = ActorSystem.Create($"start-workflow-test-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = new TraderIdentifier { UserGuid = userGuid, TraderGuid = traderGuid };

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetCurrentStatus(traderGuid).Returns(Task.FromResult(TraderStatusEnum.Running));
        lifecycle.GetTraderConfiguration(traderGuid).Returns(Task.FromResult(new TraderConfiguration
        {
            UserId = 1,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            QuoteCurrency = new CurrencySymbol("USD"),
            InitialBaseInvestment = new NonNegativeDecimalNumber(100m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(50m)
        }));

        var services = new ServiceCollection();
        services.AddSingleton(lifecycle);
        var provider = services.BuildServiceProvider();
        var lifecycleService = provider.GetRequiredService<ITraderLifecycleService>();

        var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        var dbConfig = Substitute.For<IDatabaseConfig>();
        dbConfigFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>()).Returns(dbConfig);

        var migrationService = Substitute.For<ITraderDbMigrationService>();
        migrationService.RunMigrations(Arg.Any<IDatabaseConfig>()).Returns(Task.CompletedTask);

        var startedTcs = new TaskCompletionSource<ExistingTraderStartedResponse>();

        var parentProps = Props.Create(() => new TestParentForStart(
            traderIdentifier,
            shouldReturnActorOnGet: false,
            startedTcs
        ));

        var parent = actorSystem.ActorOf(parentProps, "test-parent-start");
        parent.Tell(new CreateChildWorkflowActorStart(dbConfigFactory, migrationService, lifecycleService, traderIdentifier));

        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await lifecycle.Received(1).MarkTraderRunning(traderGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartExistingTraderWorkflow_AbortStopsRunningTraderBeforeMarkingFailed()
    {
        using var actorSystem = ActorSystem.Create($"start-workflow-abort-stop-test-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = new TraderIdentifier { UserGuid = userGuid, TraderGuid = traderGuid };

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetCurrentStatus(traderGuid).Returns(Task.FromResult(TraderStatusEnum.Created));
        lifecycle.GetTraderConfiguration(traderGuid).Returns(Task.FromResult(CreateTraderConfiguration(userGuid)));
        lifecycle.MarkTraderRunning(traderGuid).Returns(Task.FromException(new InvalidOperationException("status write failed")));
        lifecycle.MarkTraderFailed(traderGuid).Returns(Task.CompletedTask);

        var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        dbConfigFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>()).Returns(Substitute.For<IDatabaseConfig>());
        var migrationService = Substitute.For<ITraderDbMigrationService>();
        migrationService.RunMigrations(Arg.Any<IDatabaseConfig>()).Returns(Task.CompletedTask);

        var stopRequestedTcs = new TaskCompletionSource<StopTraderActorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notStartedTcs = new TaskCompletionSource<ExistingTraderNotStartedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = actorSystem.ActorOf(Props.Create(() => new StartExistingTraderAbortTestParent(
            traderIdentifier,
            dbConfigFactory,
            migrationService,
            lifecycle,
            traderActorExists: true,
            stopRequestedTcs,
            notStartedTcs
        )));

        parent.Tell(new BeginStartAbortWorkflow());

        var stopRequest = await stopRequestedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(traderIdentifier, stopRequest.TraderIdentifier);
        await lifecycle.DidNotReceive().MarkTraderFailed(traderGuid);

        parent.Tell(new CompleteStopTraderActorRequest());

        var response = await notStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(traderIdentifier, response.TraderIdentifier);
        Assert.IsType<FailedPersistingStoppedTraderError>(response.Error);
        await lifecycle.Received(1).MarkTraderFailed(traderGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartExistingTraderWorkflow_AbortWithoutRunningTraderMarksFailedAndReturnsInternalError()
    {
        using var actorSystem = ActorSystem.Create($"start-workflow-abort-no-actor-test-{Guid.NewGuid()}");

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var traderIdentifier = new TraderIdentifier { UserGuid = userGuid, TraderGuid = traderGuid };

        var lifecycle = Substitute.For<ITraderLifecycleService>();
        lifecycle.GetCurrentStatus(traderGuid).Returns(Task.FromResult(TraderStatusEnum.Created));
        lifecycle.MarkTraderFailed(traderGuid).Returns(Task.CompletedTask);

        var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        dbConfigFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>()).Returns(Substitute.For<IDatabaseConfig>());
        var migrationService = Substitute.For<ITraderDbMigrationService>();
        migrationService.RunMigrations(Arg.Any<IDatabaseConfig>())
            .Returns(Task.FromException(new InvalidOperationException("migration failed")));

        var stopRequestedTcs = new TaskCompletionSource<StopTraderActorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notStartedTcs = new TaskCompletionSource<ExistingTraderNotStartedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = actorSystem.ActorOf(Props.Create(() => new StartExistingTraderAbortTestParent(
            traderIdentifier,
            dbConfigFactory,
            migrationService,
            lifecycle,
            traderActorExists: false,
            stopRequestedTcs,
            notStartedTcs
        )));

        parent.Tell(new BeginStartAbortWorkflow());

        var response = await notStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(traderIdentifier, response.TraderIdentifier);
        Assert.IsType<FailedDatabaseMigrationsError>(response.Error);
        Assert.False(stopRequestedTcs.Task.IsCompleted);
        await lifecycle.Received(1).MarkTraderFailed(traderGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartExistingTraderWorkflow_IgnoresResponsesForDifferentTraderIdentifier()
    {
        using var actorSystem = ActorSystem.Create($"start-workflow-wrong-response-test-{Guid.NewGuid()}");

        var actualIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var wrongIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var startedTcs = new TaskCompletionSource<ExistingTraderStartedResponse>();
        var notStartedTcs = new TaskCompletionSource<ExistingTraderNotStartedResponse>();
        var parent = actorSystem.ActorOf(Props.Create(() => new WrongIdentifierStartParent(actualIdentifier, wrongIdentifier, startedTcs, notStartedTcs)));

        parent.Tell(new BeginWrongStartResponseTest());

        var completed = await Task.WhenAny(startedTcs.Task, notStartedTcs.Task, Task.Delay(500, TestContext.Current.CancellationToken));
        Assert.NotSame(startedTcs.Task, completed);
        Assert.NotSame(notStartedTcs.Task, completed);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopTraderWorkflow_IgnoresResponsesForDifferentTraderIdentifier()
    {
        using var actorSystem = ActorSystem.Create($"stop-workflow-wrong-response-test-{Guid.NewGuid()}");

        var actualIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var wrongIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var stoppedTcs = new TaskCompletionSource<TraderStoppedResponse>();
        var notStoppedTcs = new TaskCompletionSource<TraderNotStoppedResponse>();
        var parent = actorSystem.ActorOf(Props.Create(() => new WrongIdentifierStopParent(actualIdentifier, wrongIdentifier, stoppedTcs, notStoppedTcs)));

        parent.Tell(new BeginWrongStopResponseTest());

        var completed = await Task.WhenAny(stoppedTcs.Task, notStoppedTcs.Task, Task.Delay(500, TestContext.Current.CancellationToken));
        Assert.NotSame(stoppedTcs.Task, completed);
        Assert.NotSame(notStoppedTcs.Task, completed);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task DeleteTraderWorkflow_IgnoresResponsesForDifferentTraderIdentifier()
    {
        using var actorSystem = ActorSystem.Create($"delete-workflow-wrong-response-test-{Guid.NewGuid()}");

        var actualIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var wrongIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var deletedTcs = new TaskCompletionSource<TraderDeletedResponse>();
        var notDeletedTcs = new TaskCompletionSource<TraderNotDeletedResponse>();
        var parent = actorSystem.ActorOf(Props.Create(() => new WrongIdentifierDeleteParent(actualIdentifier, wrongIdentifier, deletedTcs, notDeletedTcs)));

        parent.Tell(new BeginWrongDeleteResponseTest());

        var completed = await Task.WhenAny(deletedTcs.Task, notDeletedTcs.Task, Task.Delay(500, TestContext.Current.CancellationToken));
        Assert.NotSame(deletedTcs.Task, completed);
        Assert.NotSame(notDeletedTcs.Task, completed);

        await actorSystem.Terminate();
    }

    private record CreateChildWorkflowActorStart(
        ITraderDatabaseConfigFactory DbConfigFactory,
        ITraderDbMigrationService MigrationService,
        ITraderLifecycleService LifecycleService,
        TraderIdentifier TraderIdentifier
    );

    private record BeginStartAbortWorkflow;
    private record CompleteStopTraderActorRequest;

    private sealed class StartExistingTraderAbortTestParent : ReceiveActor
    {
        private readonly TraderIdentifier TraderIdentifier;
        private readonly ITraderDatabaseConfigFactory DbConfigFactory;
        private readonly ITraderDbMigrationService MigrationService;
        private readonly ITraderLifecycleService LifecycleService;
        private readonly bool TraderActorExists;
        private readonly TaskCompletionSource<StopTraderActorRequest> StopRequestedTcs;
        private readonly TaskCompletionSource<ExistingTraderNotStartedResponse> NotStartedTcs;
        private IActorRef? StopRequestSender;

        public StartExistingTraderAbortTestParent(
            TraderIdentifier traderIdentifier,
            ITraderDatabaseConfigFactory dbConfigFactory,
            ITraderDbMigrationService migrationService,
            ITraderLifecycleService lifecycleService,
            bool traderActorExists,
            TaskCompletionSource<StopTraderActorRequest> stopRequestedTcs,
            TaskCompletionSource<ExistingTraderNotStartedResponse> notStartedTcs)
        {
            TraderIdentifier = traderIdentifier;
            DbConfigFactory = dbConfigFactory;
            MigrationService = migrationService;
            LifecycleService = lifecycleService;
            TraderActorExists = traderActorExists;
            StopRequestedTcs = stopRequestedTcs;
            NotStartedTcs = notStartedTcs;

            Receive<BeginStartAbortWorkflow>(_ => StartWorkflow());
            Receive<GetTraderActorRequest>(_ => Sender.Tell(new GetTraderActorResponse
            {
                Result = TraderActorExists
                    ? new Result<IActorRef>.Ok(ActorRefs.Nobody)
                    : new Result<IActorRef>.Error(new TraderActorNotFoundError("not found"))
            }));
            Receive<StartTraderActorRequest>(request => Sender.Tell(new TraderActorStartedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Config = request.Config
            }));
            Receive<StopTraderActorRequest>(request =>
            {
                StopRequestSender = Sender;
                StopRequestedTcs.TrySetResult(request);
            });
            Receive<CompleteStopTraderActorRequest>(_ =>
            {
                StopRequestSender?.Tell(new TraderActorStoppedResponse { TraderIdentifier = TraderIdentifier });
                StopRequestSender = null;
            });
            Receive<ExistingTraderNotStartedResponse>(response => NotStartedTcs.TrySetResult(response));
        }

        private void StartWorkflow()
        {
            var child = Context.ActorOf(Props.Create(() => new StartExistingTraderWorkflowActor(
                traderDbConfigFactory: DbConfigFactory,
                traderMigrationService: MigrationService,
                lifecycleService: LifecycleService,
                logger: NullLogger<StartExistingTraderWorkflowActor>.Instance,
                traderIdentifier: TraderIdentifier
            )));

            child.Tell(new StartExistingTraderWorkflowActor.StartWorkflowActor());
        }
    }

    private class TestParentForStart : ReceiveActor
    {
        private readonly TraderIdentifier TraderIdentifier;
        private readonly bool ShouldReturnActorOnGet;
        private readonly TaskCompletionSource<ExistingTraderStartedResponse> StartedTcs;
        private IActorRef? ChildRef;

        public TestParentForStart(TraderIdentifier traderIdentifier, bool shouldReturnActorOnGet, TaskCompletionSource<ExistingTraderStartedResponse> startedTcs)
        {
            TraderIdentifier = traderIdentifier;
            ShouldReturnActorOnGet = shouldReturnActorOnGet;
            StartedTcs = startedTcs;

            Receive<CreateChildWorkflowActorStart>(m => HandleCreateChild(m));
            Receive<GetTraderActorRequest>(m => HandleGetTraderActorRequest(m));
            Receive<StartTraderActorRequest>(m => HandleStartTraderActorRequest(m));
            Receive<ExistingTraderStartedResponse>(m => HandleExistingStartedResponse(m));
        }

        private void HandleCreateChild(CreateChildWorkflowActorStart m)
        {
            var child = Context.ActorOf(Props.Create(() => new StartExistingTraderWorkflowActor(
                traderDbConfigFactory: m.DbConfigFactory,
                traderMigrationService: m.MigrationService,
                lifecycleService: m.LifecycleService,
                logger: NullLogger<StartExistingTraderWorkflowActor>.Instance,
                traderIdentifier: m.TraderIdentifier
            )));

            ChildRef = child;
            child.Tell(new StartExistingTraderWorkflowActor.StartWorkflowActor());
        }

        private void HandleGetTraderActorRequest(GetTraderActorRequest req)
        {
            if (ShouldReturnActorOnGet && ChildRef is not null)
            {
                Sender.Tell(new GetTraderActorResponse { Result = new Result<IActorRef>.Ok(ChildRef) });
            }
            else
            {
                Sender.Tell(new GetTraderActorResponse { Result = new Result<IActorRef>.Error(new TraderActorNotFoundError("not found")) });
            }
        }

        private void HandleStartTraderActorRequest(StartTraderActorRequest req)
        {
            Sender.Tell(new TraderActorStartedResponse { TraderIdentifier = req.TraderIdentifier, Config = req.Config });
        }

        private void HandleExistingStartedResponse(ExistingTraderStartedResponse res)
        {
            StartedTcs.TrySetResult(res);
        }
    }

    private record BeginWrongStartResponseTest;
    private record BeginWrongStopResponseTest;
    private record BeginWrongDeleteResponseTest;

    private sealed class WrongIdentifierStartParent : ReceiveActor
    {
        public WrongIdentifierStartParent(
            TraderIdentifier actualIdentifier,
            TraderIdentifier wrongIdentifier,
            TaskCompletionSource<ExistingTraderStartedResponse> startedTcs,
            TaskCompletionSource<ExistingTraderNotStartedResponse> notStartedTcs)
        {
            var lifecycle = Substitute.For<ITraderLifecycleService>();
            lifecycle.GetCurrentStatus(actualIdentifier.TraderGuid).Returns(Task.FromResult(TraderStatusEnum.Created));
            lifecycle.GetTraderConfiguration(actualIdentifier.TraderGuid).Returns(Task.FromResult(CreateTraderConfiguration(actualIdentifier.UserGuid)));

            var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
            dbConfigFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>()).Returns(Substitute.For<IDatabaseConfig>());

            var migrationService = Substitute.For<ITraderDbMigrationService>();
            migrationService.RunMigrations(Arg.Any<IDatabaseConfig>()).Returns(Task.CompletedTask);

            Receive<BeginWrongStartResponseTest>(_ =>
            {
                var child = Context.ActorOf(Props.Create(() => new StartExistingTraderWorkflowActor(
                    traderDbConfigFactory: dbConfigFactory,
                    traderMigrationService: migrationService,
                    lifecycleService: lifecycle,
                    logger: NullLogger<StartExistingTraderWorkflowActor>.Instance,
                    traderIdentifier: actualIdentifier
                )));

                child.Tell(new StartExistingTraderWorkflowActor.StartWorkflowActor());
            });

            Receive<StartTraderActorRequest>(request =>
                Sender.Tell(new TraderActorStartedResponse
                {
                    TraderIdentifier = wrongIdentifier,
                    Config = request.Config
                }));

            Receive<ExistingTraderStartedResponse>(response => startedTcs.TrySetResult(response));
            Receive<ExistingTraderNotStartedResponse>(response => notStartedTcs.TrySetResult(response));
        }
    }

    private sealed class WrongIdentifierStopParent : ReceiveActor
    {
        public WrongIdentifierStopParent(
            TraderIdentifier actualIdentifier,
            TraderIdentifier wrongIdentifier,
            TaskCompletionSource<TraderStoppedResponse> stoppedTcs,
            TaskCompletionSource<TraderNotStoppedResponse> notStoppedTcs)
        {
            var lifecycle = Substitute.For<ITraderLifecycleService>();
            lifecycle.LoadTrader(actualIdentifier.TraderGuid).Returns(Task.FromResult(new TraderEntity
            {
                Guid = actualIdentifier.TraderGuid,
                UserGuid = actualIdentifier.UserGuid,
                Status = TraderStatusEnum.Running,
                BrokerName = BrokerEnum.IBKR,
                StrategyName = TradingStrategyEnum.SpotGrid,
                BaseCurrency = new CurrencySymbol("BTC"),
                QuoteCurrency = new CurrencySymbol("USD"),
                InitialBaseBalance = new NonNegativeDecimalNumber(1m),
                InitialQuoteBalance = new NonNegativeDecimalNumber(100m),
                InitialPrice = new NonNegativeDecimalNumber(10m),
                InitialTotalValue = new NonNegativeDecimalNumber(110m),
                UserId = 1,
                FirstStartedAt = DateTimeOffset.UtcNow,
                LastStartedAt = DateTimeOffset.UtcNow
            }));

            Receive<BeginWrongStopResponseTest>(_ =>
            {
                var child = Context.ActorOf(Props.Create(() => new StopTraderWorkflowActor(
                    lifecycleService: lifecycle,
                    traderDbConfigFactory: Substitute.For<ITraderDatabaseConfigFactory>(),
                    positionRepositoryFactory: Substitute.For<IPositionRepositoryFactory>(),
                    traderConfigTransformer: new TraderEntityToTraderConfigurationTransformer(),
                    logger: NullLogger<StopTraderWorkflowActor>.Instance,
                    traderIdentifier: actualIdentifier,
                    brokerClientSupervisorReference: ActorRefs.Nobody
                )));

                child.Tell(new StopTraderWorkflowActor.StartWorkflowActor());
            });

            Receive<StopTraderActorRequest>(request =>
                Sender.Tell(new TraderActorStoppedResponse
                {
                    TraderIdentifier = wrongIdentifier
                }));

            Receive<TraderStoppedResponse>(response => stoppedTcs.TrySetResult(response));
            Receive<TraderNotStoppedResponse>(response => notStoppedTcs.TrySetResult(response));
        }
    }

    private sealed class WrongIdentifierDeleteParent : ReceiveActor
    {
        public WrongIdentifierDeleteParent(
            TraderIdentifier actualIdentifier,
            TraderIdentifier wrongIdentifier,
            TaskCompletionSource<TraderDeletedResponse> deletedTcs,
            TaskCompletionSource<TraderNotDeletedResponse> notDeletedTcs)
        {
            Receive<BeginWrongDeleteResponseTest>(_ =>
            {
                var child = Context.ActorOf(Props.Create(() => new DeleteTraderWorkflowActor(
                    lifecycleService: Substitute.For<ITraderLifecycleService>(),
                    traderDbConfigFactory: Substitute.For<ITraderDatabaseConfigFactory>(),
                    logger: NullLogger<DeleteTraderWorkflowActor>.Instance,
                    traderIdentifier: actualIdentifier
                )));

                child.Tell(new DeleteTraderWorkflowActor.StartWorkflowActor());
            });

            Receive<StopTraderActorRequest>(request =>
                Sender.Tell(new TraderActorStoppedResponse
                {
                    TraderIdentifier = wrongIdentifier
                }));

            Receive<TraderDeletedResponse>(response => deletedTcs.TrySetResult(response));
            Receive<TraderNotDeletedResponse>(response => notDeletedTcs.TrySetResult(response));
        }
    }

    private static TraderConfiguration CreateTraderConfiguration(NonEmptyGuid userGuid)
    {
        return new TraderConfiguration
        {
            UserId = 1,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            QuoteCurrency = new CurrencySymbol("USD"),
            InitialBaseInvestment = new NonNegativeDecimalNumber(100m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(50m)
        };
    }
}
