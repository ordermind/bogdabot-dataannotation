using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Trading.Actors;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Transformers;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopAllTradersForUserProtocol;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests
{
    public class PerUserTraderSupervisorStopTraderTests
    {
        [Fact]
        public async Task StopAllTraders_NoActiveTraders_SendsAllTradersStopped()
        {
            using var actorSystem = ActorSystem.Create($"per-user-stop-none-{Guid.NewGuid()}");

            var userGuid = NonEmptyGuid.NewGuid();

            var lifecycle = Substitute.For<ITraderLifecycleService>();
            lifecycle.GetActiveTradersForUser(userGuid).Returns(Task.FromResult<IEnumerable<TraderIdentifier>>(Array.Empty<TraderIdentifier>()));

            var services = new ServiceCollection();
            services.AddSingleton(lifecycle);
            var provider = services.BuildServiceProvider();
            var lifecycleService = provider.GetRequiredService<ITraderLifecycleService>();

            var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
            var migrationService = Substitute.For<Bogdabot.Server.Shared.Database.ITraderDbMigrationService>();
            var actorTaskStoreFactory = new IntegrationActorTaskStoreFactory();

            var parentProps = Props.Create(() => new TestParentForStopAll(lifecycleService, dbConfigFactory, migrationService));
            var parent = actorSystem.ActorOf(parentProps, "test-parent-stop-all-none");

            // Tell parent to create the PerUserTraderSupervisor child
            var childRef = await parent.Ask<IActorRef>(new CreateChildPerUserSupervisor(userGuid, actorTaskStoreFactory), TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);

            // Ask the child to stop all traders via the parent
            var stopped = await parent.Ask<StopAllTradersResponse>(new StopAllTradersRequest { UserGuid = userGuid }, TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);

            Assert.IsType<AllTradersStoppedResponse>(stopped);

            await actorSystem.Terminate();
        }

        [Fact]
        public async Task StopAllTraders_DbError_ReturnsNotStopped()
        {
            using var actorSystem = ActorSystem.Create($"per-user-stop-error-{Guid.NewGuid()}");

            var userGuid = NonEmptyGuid.NewGuid();

            var lifecycle = Substitute.For<ITraderLifecycleService>();
            var activeTraderQueryCount = 0;
            lifecycle.GetActiveTradersForUser(userGuid).Returns(_ =>
            {
                if (Interlocked.Increment(ref activeTraderQueryCount) == 1)
                {
                    return Task.FromResult<IEnumerable<TraderIdentifier>>([]);
                }

                throw new InvalidOperationException("boom");
            });

            var services = new ServiceCollection();
            services.AddSingleton(lifecycle);
            var provider = services.BuildServiceProvider();
            var lifecycleService = provider.GetRequiredService<ITraderLifecycleService>();

            var dbConfigFactory = Substitute.For<ITraderDatabaseConfigFactory>();
            var migrationService = Substitute.For<Bogdabot.Server.Shared.Database.ITraderDbMigrationService>();
            var actorTaskStoreFactory = new IntegrationActorTaskStoreFactory();

            var parentProps = Props.Create(() => new TestParentForStopAll(lifecycleService, dbConfigFactory, migrationService));
            var parent = actorSystem.ActorOf(parentProps, "test-parent-stop-all-error");

            var childRef = await parent.Ask<IActorRef>(new CreateChildPerUserSupervisor(userGuid, actorTaskStoreFactory), TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);

            var response = await parent.Ask<StopAllTradersResponse>(new StopAllTradersRequest { UserGuid = userGuid }, TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);

            Assert.IsType<AllTradersNotStoppedResponse>(response);

            await actorSystem.Terminate();
        }

        private record CreateChildPerUserSupervisor(NonEmptyGuid UserGuid, IActorTaskStoreFactory ActorTaskStoreFactory);

        private class TestParentForStopAll : ReceiveActor
        {
            private readonly ITraderLifecycleService LifecycleService;
            private readonly ITraderDatabaseConfigFactory DbConfigFactory;
            private readonly Bogdabot.Server.Shared.Database.ITraderDbMigrationService MigrationService;
            private IActorRef? ChildRef;
            private readonly Dictionary<NonEmptyGuid, IActorRef> PendingStopRequestors = new();
            private NonEmptyGuid? CreatedChildUserGuid;

            public TestParentForStopAll(ITraderLifecycleService lifecycleService, ITraderDatabaseConfigFactory dbConfigFactory, Bogdabot.Server.Shared.Database.ITraderDbMigrationService migrationService)
            {
                LifecycleService = lifecycleService;
                DbConfigFactory = dbConfigFactory;
                MigrationService = migrationService;

                Receive<CreateChildPerUserSupervisor>(m => HandleCreate(m));
                Receive<StopAllTradersRequest>(m => HandleStopAll(m));
                Receive<AllTradersStoppedResponse>(m => HandleStoppedResponse(m));
                Receive<AllTradersNotStoppedResponse>(m => HandleNotStoppedResponse(m));
                Receive<Terminated>(t => HandleChildTerminated(t));
                Receive<TradingRootSupervisor.SupervisorInitializedMessage>(_ => { /* ignore to avoid dead letters */ });
            }

            private void HandleCreate(CreateChildPerUserSupervisor m)
            {
                var actorTaskStoreFactory = m.ActorTaskStoreFactory;

                ChildRef = Context.ActorOf(Props.Create(() => new PerUserTraderSupervisor(
                    LifecycleService,
                    new TraderActorFactory(DbConfigFactory, MigrationService, NullLoggerFactory.Instance),
                    new TraderWorkflowActorFactory(
                        DbConfigFactory,
                        MigrationService,
                        LifecycleService,
                        Substitute.For<IPositionRepositoryFactory>(),
                        new TraderEntityToTraderConfigurationTransformer(),
                        NullLoggerFactory.Instance
                    ),
                    actorTaskStoreFactory,
                    ActorRefs.Nobody,
                    NullLogger<PerUserTraderSupervisor>.Instance,
                    m.UserGuid
                )));
                // reply with created child so tests can wait deterministically
                CreatedChildUserGuid = m.UserGuid;
                Context.Watch(ChildRef);

                Sender.Tell(ChildRef);
            }

            private void HandleChildTerminated(Terminated t)
            {
                if (CreatedChildUserGuid.HasValue && PendingStopRequestors.TryGetValue(CreatedChildUserGuid.Value, out var orig))
                {
                    orig.Tell(new AllTradersNotStoppedResponse { UserGuid = CreatedChildUserGuid.Value, Error = new Bogdabot.Server.Shared.Errors.TraderSupervisorStopUserError("child-terminated") });
                    PendingStopRequestors.Remove(CreatedChildUserGuid.Value);
                }
            }

            private void HandleStopAll(StopAllTradersRequest m)
            {
                if (ChildRef is null)
                {
                    Sender.Tell(new AllTradersNotStoppedResponse { UserGuid = m.UserGuid, Error = new Bogdabot.Server.Shared.Errors.TraderSupervisorStopUserError("no child") });
                    return;
                }

                // Act as the parent: remember the original requester and forward the message with me as sender
                PendingStopRequestors[m.UserGuid] = Sender;
                ChildRef.Tell(m, Self);
            }

            private void HandleStoppedResponse(AllTradersStoppedResponse m)
            {
                if (PendingStopRequestors.TryGetValue(m.UserGuid, out var orig))
                {
                    orig.Tell(m);
                    PendingStopRequestors.Remove(m.UserGuid);
                }
            }

            private void HandleNotStoppedResponse(AllTradersNotStoppedResponse m)
            {
                if (PendingStopRequestors.TryGetValue(m.UserGuid, out var orig))
                {
                    orig.Tell(m);
                    PendingStopRequestors.Remove(m.UserGuid);
                }
            }
        }

        private class IntegrationActorTaskStoreFactory : IActorTaskStoreFactory
        {
            public ActorTaskStore<TTask> Create<TTask>() where TTask : notnull
            {
                return new ActorTaskStore<TTask>();
            }
        }
    }
}
