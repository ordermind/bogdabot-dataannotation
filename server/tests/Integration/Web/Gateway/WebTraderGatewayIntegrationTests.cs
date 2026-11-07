using Akka.Actor;
using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Web.Gateway;
using NSubstitute;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.DeleteTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Tests.Integration.Web.Gateway;

public class WebTraderGatewayIntegrationTests
{
    [Fact]
    public async Task CreateTrader_ReturnsGuid_WhenSupervisorReturnsStarted()
    {
        using var actorSystem = ActorSystem.Create($"gateway-create-ok-{Guid.NewGuid()}");

        var expectedGuid = NonEmptyGuid.NewGuid();
        var supervisor = actorSystem.ActorOf(Props.Create(() => new CreateTraderReplyActor(expectedGuid)));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        var result = await sut.CreateTrader(CreateConfig());

        var ok = Assert.IsType<Result<NonEmptyGuid>.Ok>(result);
        Assert.Equal(expectedGuid, ok.Value);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task CreateTrader_ReturnsError_WhenSupervisorReturnsNotStarted()
    {
        using var actorSystem = ActorSystem.Create($"gateway-create-error-{Guid.NewGuid()}");

        var supervisor = actorSystem.ActorOf(Props.Create(() => new CreateTraderNotStartedReplyActor()));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        var result = await sut.CreateTrader(CreateConfig());

        var error = Assert.IsType<Result<NonEmptyGuid>.Error>(result);
        Assert.Contains("could not be started", error.ErrorValue.ToString());

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartStopDelete_ReturnSuccess_WhenSupervisorRepliesSuccess()
    {
        using var actorSystem = ActorSystem.Create($"gateway-multi-ok-{Guid.NewGuid()}");

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var supervisor = actorSystem.ActorOf(Props.Create(() => new SuccessReplyActor(traderIdentifier)));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        Assert.Equal(traderIdentifier.TraderGuid, Assert.IsType<Result<NonEmptyGuid>.Ok>(await sut.StartActiveTrader(traderIdentifier)).Value);
        Assert.Equal(traderIdentifier.TraderGuid, Assert.IsType<Result<NonEmptyGuid>.Ok>(await sut.StopOneTrader(traderIdentifier)).Value);
        Assert.Equal(traderIdentifier.TraderGuid, Assert.IsType<Result<NonEmptyGuid>.Ok>(await sut.DeleteTrader(traderIdentifier)).Value);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartStopDelete_ReturnError_WhenSupervisorRepliesFailure()
    {
        using var actorSystem = ActorSystem.Create($"gateway-multi-error-{Guid.NewGuid()}");

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var supervisor = actorSystem.ActorOf(Props.Create(() => new ErrorReplyActor(traderIdentifier)));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        Assert.IsType<Result<NonEmptyGuid>.Error>(await sut.StartActiveTrader(traderIdentifier));
        Assert.IsType<Result<NonEmptyGuid>.Error>(await sut.StopOneTrader(traderIdentifier));
        Assert.IsType<Result<NonEmptyGuid>.Error>(await sut.DeleteTrader(traderIdentifier));

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task Methods_ThrowActorNotFound_WhenSupervisorMissing()
    {
        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult<IActorRef>(ActorRefs.Nobody));

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var sut = new WebTraderGateway(rootActor);

        await Assert.ThrowsAsync<ActorNotFoundException>(() => sut.CreateTrader(CreateConfig()));
        await Assert.ThrowsAsync<ActorNotFoundException>(() => sut.StartActiveTrader(traderIdentifier));
        await Assert.ThrowsAsync<ActorNotFoundException>(() => sut.StopOneTrader(traderIdentifier));
        await Assert.ThrowsAsync<ActorNotFoundException>(() => sut.DeleteTrader(traderIdentifier));
    }

    [Fact]
    public async Task CreateTrader_ThrowsInvalidOperationException_WhenSupervisorReturnsUnexpectedResponseType()
    {
        using var actorSystem = ActorSystem.Create($"gateway-create-unexpected-{Guid.NewGuid()}");

        var supervisor = actorSystem.ActorOf(Props.Create(() => new UnexpectedReplyActor()));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateTrader(CreateConfig()));

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartStoppedTrader_ThrowsInvalidOperationException_WhenSupervisorReturnsUnexpectedResponseType()
    {
        using var actorSystem = ActorSystem.Create($"gateway-start-unexpected-{Guid.NewGuid()}");

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var supervisor = actorSystem.ActorOf(Props.Create(() => new UnexpectedReplyActor()));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartActiveTrader(traderIdentifier));

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StopTrader_ThrowsInvalidOperationException_WhenSupervisorReturnsUnexpectedResponseType()
    {
        using var actorSystem = ActorSystem.Create($"gateway-stop-unexpected-{Guid.NewGuid()}");

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var supervisor = actorSystem.ActorOf(Props.Create(() => new UnexpectedReplyActor()));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StopOneTrader(traderIdentifier));

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task DeleteTrader_ThrowsInvalidOperationException_WhenSupervisorReturnsUnexpectedResponseType()
    {
        using var actorSystem = ActorSystem.Create($"gateway-delete-unexpected-{Guid.NewGuid()}");

        var traderIdentifier = new TraderIdentifier
        {
            UserGuid = NonEmptyGuid.NewGuid(),
            TraderGuid = NonEmptyGuid.NewGuid()
        };

        var supervisor = actorSystem.ActorOf(Props.Create(() => new UnexpectedReplyActor()));

        var rootActor = Substitute.For<IAkkaHost>();
        rootActor.GetTraderSupervisorForUser(Arg.Any<NonEmptyGuid>()).Returns(Task.FromResult(supervisor));

        var sut = new WebTraderGateway(rootActor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteTrader(traderIdentifier));

        await actorSystem.Terminate();
    }

    private static TraderConfiguration CreateConfig()
    {
        return new TraderConfiguration
        {
            UserId = 1,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialBaseInvestment = new NonNegativeDecimalNumber(100m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(50m)
        };
    }

    private sealed class CreateTraderReplyActor : ReceiveActor
    {
        public CreateTraderReplyActor(NonEmptyGuid traderGuid)
        {
            Receive<StartNewTraderRequest>(request =>
            {
                Sender.Tell(new ExistingTraderStartedResponse
                {
                    TraderIdentifier = new TraderIdentifier
                    {
                        UserGuid = request.Config.UserGuid,
                        TraderGuid = traderGuid
                    }
                });
            });
        }
    }

    private sealed class CreateTraderNotStartedReplyActor : ReceiveActor
    {
        public CreateTraderNotStartedReplyActor()
        {
            Receive<StartNewTraderRequest>(request =>
            {
                Sender.Tell(new NewTraderNotStartedResponse
                {
                    Config = request.Config,
                    Error = new TraderStartUserError("could not be started")
                });
            });
        }
    }

    private sealed class SuccessReplyActor : ReceiveActor
    {
        public SuccessReplyActor(TraderIdentifier traderIdentifier)
        {
            Receive<StartActiveTraderRequest>(_ =>
                Sender.Tell(new ExistingTraderStartedResponse { TraderIdentifier = traderIdentifier }));

            Receive<StopTraderRequest>(_ =>
                Sender.Tell(new TraderStoppedResponse { TraderIdentifier = traderIdentifier }));

            Receive<DeleteTraderRequest>(_ =>
                Sender.Tell(new TraderDeletedResponse { TraderIdentifier = traderIdentifier }));
        }
    }

    private sealed class ErrorReplyActor : ReceiveActor
    {
        public ErrorReplyActor(TraderIdentifier traderIdentifier)
        {
            Receive<StartActiveTraderRequest>(_ =>
                Sender.Tell(new ExistingTraderNotStartedResponse
                {
                    TraderIdentifier = traderIdentifier,
                    Error = new TraderStartUserError("start failed")
                }));

            Receive<StopTraderRequest>(_ =>
                Sender.Tell(new TraderNotStoppedResponse
                {
                    TraderIdentifier = traderIdentifier,
                    Error = new TraderStopUserError("stop failed")
                }));

            Receive<DeleteTraderRequest>(_ =>
                Sender.Tell(new TraderNotDeletedResponse
                {
                    TraderIdentifier = traderIdentifier,
                    Error = new TraderDeleteUserError("delete failed")
                }));
        }
    }

    private sealed class UnexpectedReplyActor : ReceiveActor
    {
        public UnexpectedReplyActor()
        {
            Receive<StartNewTraderRequest>(_ => Sender.Tell("unexpected"));
            Receive<StartActiveTraderRequest>(_ => Sender.Tell("unexpected"));
            Receive<StopTraderRequest>(_ => Sender.Tell("unexpected"));
            Receive<DeleteTraderRequest>(_ => Sender.Tell("unexpected"));
        }
    }
}
