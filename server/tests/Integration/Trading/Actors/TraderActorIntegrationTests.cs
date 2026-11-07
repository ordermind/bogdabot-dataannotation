using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors;

public class TraderActorIntegrationTests
{
    [Fact]
    public async Task PreStart_SendsTraderActorStartedResponse()
    {
        using var actorSystem = ActorSystem.Create($"trader-actor-start-tests-{Guid.NewGuid()}");

        var startedTcs = new TaskCompletionSource<TraderActorStartedResponse>();
        var stoppedTcs = new TaskCompletionSource<TraderActorStoppedResponse>();
        var notStartedTcs = new TaskCompletionSource<TraderActorNotStartedResponse>();
        var terminatedTcs = new TaskCompletionSource<Terminated>();

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var parent = actorSystem.ActorOf(Props.Create(() =>
            new TraderActorParentHarness(
                CreateDbConfigFactory(),
                CreateMigrationService(),
                CreateConfig(traderIdentifier.UserGuid),
                traderIdentifier,
                startedTcs,
                stoppedTcs,
                notStartedTcs,
                terminatedTcs
            )));

        var started = await WaitFor(startedTcs.Task);

        Assert.Equal(traderIdentifier, started.TraderIdentifier);
        Assert.Equal(traderIdentifier.UserGuid, started.Config.UserGuid);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopRequest_SendsTraderActorStoppedResponse_AndTerminatesActor()
    {
        using var actorSystem = ActorSystem.Create($"trader-actor-stop-tests-{Guid.NewGuid()}");

        var startedTcs = new TaskCompletionSource<TraderActorStartedResponse>();
        var stoppedTcs = new TaskCompletionSource<TraderActorStoppedResponse>();
        var notStartedTcs = new TaskCompletionSource<TraderActorNotStartedResponse>();
        var terminatedTcs = new TaskCompletionSource<Terminated>();

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var parent = actorSystem.ActorOf(Props.Create(() =>
            new TraderActorParentHarness(
                CreateDbConfigFactory(),
                CreateMigrationService(),
                CreateConfig(traderIdentifier.UserGuid),
                traderIdentifier,
                startedTcs,
                stoppedTcs,
                notStartedTcs,
                terminatedTcs
            )));

        await WaitFor(startedTcs.Task);

        parent.Tell(new StopTraderActorRequest
        {
            TraderIdentifier = traderIdentifier
        });

        var stopped = await WaitFor(stoppedTcs.Task);
        var terminated = await WaitFor(terminatedTcs.Task);

        Assert.Equal(traderIdentifier, stopped.TraderIdentifier);
        Assert.Equal(TraderActorId.FromTraderGuid(traderIdentifier.TraderGuid), terminated.ActorRef.Path.Name);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task PreStart_StopMessage_IsStashedAndHandledAfterStart()
    {
        using var actorSystem = ActorSystem.Create($"trader-actor-stash-tests-{Guid.NewGuid()}");

        var startedTcs = new TaskCompletionSource<TraderActorStartedResponse>();
        var stoppedTcs = new TaskCompletionSource<TraderActorStoppedResponse>();
        var notStartedTcs = new TaskCompletionSource<TraderActorNotStartedResponse>();
        var terminatedTcs = new TaskCompletionSource<Terminated>();

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var parent = actorSystem.ActorOf(Props.Create(() =>
            new TraderActorParentHarness(
                CreateDbConfigFactory(),
                CreateMigrationService(),
                CreateConfig(traderIdentifier.UserGuid),
                traderIdentifier,
                startedTcs,
                stoppedTcs,
                notStartedTcs,
                terminatedTcs
            )));

        // Send Stop before waiting for start; should be stashed and handled after start
        parent.Tell(new StopTraderActorRequest
        {
            TraderIdentifier = traderIdentifier
        });

        var started = await WaitFor(startedTcs.Task);
        var stopped = await WaitFor(stoppedTcs.Task);
        var terminated = await WaitFor(terminatedTcs.Task);

        Assert.Equal(traderIdentifier, started.TraderIdentifier);
        Assert.Equal(traderIdentifier, stopped.TraderIdentifier);
        Assert.Equal(TraderActorId.FromTraderGuid(traderIdentifier.TraderGuid), terminated.ActorRef.Path.Name);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopRequest_WhenIdentifierDoesNotMatch_RejectsMessageWithIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"trader-actor-wrong-identifier-tests-{Guid.NewGuid()}");

        var startedTcs = new TaskCompletionSource<TraderActorStartedResponse>();
        var stoppedTcs = new TaskCompletionSource<TraderActorStoppedResponse>();
        var notStoppedTcs = new TaskCompletionSource<TraderActorNotStoppedResponse>();
        var terminatedTcs = new TaskCompletionSource<Terminated>();

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var wrongIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var parent = actorSystem.ActorOf(Props.Create(() =>
            new TraderActorParentHarness(
                CreateDbConfigFactory(),
                CreateMigrationService(),
                CreateConfig(traderIdentifier.UserGuid),
                traderIdentifier,
                startedTcs,
                stoppedTcs,
                notStartedTcs: new TaskCompletionSource<TraderActorNotStartedResponse>(),
                terminatedTcs: terminatedTcs,
                notStoppedTcs: notStoppedTcs
            )));

        await WaitFor(startedTcs.Task);

        parent.Tell(new StopTraderActorRequest
        {
            TraderIdentifier = wrongIdentifier
        });

        var notStopped = await WaitFor(notStoppedTcs.Task);

        Assert.Equal(wrongIdentifier, notStopped.TraderIdentifier);
        Assert.IsType<IllegalActorMessageError>(notStopped.Error);

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
            InitialBaseInvestment = new NonNegativeDecimalNumber(10m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(5m)
        };
    }

    private static ITraderDatabaseConfigFactory CreateDbConfigFactory()
    {
        var factory = Substitute.For<ITraderDatabaseConfigFactory>();
        var config = Substitute.For<IDatabaseConfig>();

        factory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>()).Returns(config);

        return factory;
    }

    private static ITraderDbMigrationService CreateMigrationService()
    {
        var service = Substitute.For<ITraderDbMigrationService>();
        service.RunMigrations(Arg.Any<IDatabaseConfig>()).Returns(Task.CompletedTask);
        return service;
    }

    private static async Task<T> WaitFor<T>(Task<T> task)
    {
        return await task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    private sealed class TraderActorParentHarness : ReceiveActor
    {
        public TraderActorParentHarness(
            ITraderDatabaseConfigFactory dbConfigFactory,
            ITraderDbMigrationService migrationService,
            TraderConfiguration config,
            TraderIdentifier traderIdentifier,
            TaskCompletionSource<TraderActorStartedResponse> startedTcs,
            TaskCompletionSource<TraderActorStoppedResponse> stoppedTcs,
            TaskCompletionSource<TraderActorNotStartedResponse> notStartedTcs,
            TaskCompletionSource<Terminated> terminatedTcs,
            TaskCompletionSource<TraderActorNotStoppedResponse>? notStoppedTcs = null
        )
        {
            var child = Context.ActorOf(
                Props.Create(() => new TraderActor(
                    dbConfigFactory,
                    migrationService,
                    config,
                    traderIdentifier,
                    NullLogger<TraderActor>.Instance
                )),
                TraderActorId.FromTraderGuid(traderIdentifier.TraderGuid)
            );

            Context.Watch(child);

            Receive<TraderActorStartedResponse>(message => startedTcs.TrySetResult(message));
            Receive<TraderActorStoppedResponse>(message => stoppedTcs.TrySetResult(message));
            Receive<TraderActorNotStartedResponse>(message => notStartedTcs.TrySetResult(message));
            Receive<TraderActorNotStoppedResponse>(message => notStoppedTcs?.TrySetResult(message));
            Receive<Terminated>(message => terminatedTcs.TrySetResult(message));
            Receive<StopTraderActorRequest>(message => child.Tell(message));
        }
    }
}
