using Akka.Actor;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.GetCurrentPriceProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.StartBrokerClientProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetBrokerClientProtocol;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors;

public class PerUserBrokerClientSupervisorIntegrationTests
{
    [Fact]
    public async Task GetBrokerClientActor_CreatesOnceAndReturnsCurrentPriceResponse()
    {
        using var actorSystem = ActorSystem.Create($"broker-client-supervisor-tests-{Guid.NewGuid()}");
        var loggerFactory = NullLoggerFactory.Instance;
        var userGuid = NonEmptyGuid.NewGuid();
        var supervisor = actorSystem.ActorOf(Props.Create(() => new PerUserBrokerClientSupervisor(
            new ActorTaskStoreFactory(),
            loggerFactory,
            NullLogger<PerUserBrokerClientSupervisor>.Instance,
            userGuid)));
        var request = new GetBrokerClientActorRequest
        {
            Identifier = new BrokerClientIdentifier
            {
                UserGuid = userGuid,
                BrokerName = BrokerEnum.IBKR
            }
        };

        var firstResponse = await supervisor.Ask<GetBrokerClientActorResponse>(request, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var secondResponse = await supervisor.Ask<GetBrokerClientActorResponse>(request, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        var firstActor = Assert.IsType<Result<IActorRef>.Ok>(firstResponse.Result).Value;
        var secondActor = Assert.IsType<Result<IActorRef>.Ok>(secondResponse.Result).Value;
        Assert.Same(firstActor, secondActor);
        Assert.Equal("brokerclient-IBKR", firstActor.Path.Name);

        var priceResponse = await firstActor.Ask<GetCurrentPriceResponse>(new GetCurrentPriceRequest
        {
            BaseCurrency = new CurrencySymbol("BTC")
        }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(new CurrencySymbol("BTC"), priceResponse.BaseCurrency);
        var price = Assert.IsType<Result<NonNegativeDecimalNumber>.Ok>(priceResponse.Result);
        Assert.Equal(0m, price.Value.Value);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task GetBrokerClientActor_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"broker-client-supervisor-cross-user-tests-{Guid.NewGuid()}");
        var loggerFactory = NullLoggerFactory.Instance;
        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var supervisor = actorSystem.ActorOf(Props.Create(() => new PerUserBrokerClientSupervisor(
            new ActorTaskStoreFactory(),
            loggerFactory,
            NullLogger<PerUserBrokerClientSupervisor>.Instance,
            userGuid)));

        var response = await supervisor.Ask<GetBrokerClientActorResponse>(
            new GetBrokerClientActorRequest
            {
                Identifier = new BrokerClientIdentifier
                {
                    UserGuid = wrongUserGuid,
                    BrokerName = BrokerEnum.IBKR
                }
            },
            TestContext.Current.CancellationToken);

        var error = Assert.IsType<Result<IActorRef>.Error>(response.Result);
        Assert.IsType<IllegalActorMessageError>(error.ErrorValue);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task StartBrokerClientActorResponse_WhenUserGuidDoesNotMatchSupervisor_ReturnsIllegalActorMessageError()
    {
        using var actorSystem = ActorSystem.Create($"broker-client-supervisor-response-cross-user-tests-{Guid.NewGuid()}");
        var loggerFactory = NullLoggerFactory.Instance;
        var userGuid = NonEmptyGuid.NewGuid();
        var wrongUserGuid = NonEmptyGuid.NewGuid();
        var supervisor = actorSystem.ActorOf(Props.Create(() => new PerUserBrokerClientSupervisor(
            new ActorTaskStoreFactory(),
            loggerFactory,
            NullLogger<PerUserBrokerClientSupervisor>.Instance,
            userGuid)));

        var response = await supervisor.Ask<GetBrokerClientActorResponse>(
            new BrokerClientActorNotStartedResponse
            {
                Identifier = new BrokerClientIdentifier
                {
                    UserGuid = wrongUserGuid,
                    BrokerName = BrokerEnum.IBKR
                },
                Config = new BrokerClientConfiguration
                {
                    UserGuid = wrongUserGuid,
                    BrokerName = BrokerEnum.IBKR
                },
                Error = new BrokerClientStartUserError("wrong supervisor")
            },
            TestContext.Current.CancellationToken);

        var error = Assert.IsType<Result<IActorRef>.Error>(response.Result);
        Assert.IsType<IllegalActorMessageError>(error.ErrorValue);

        await actorSystem.Terminate();
    }
}
