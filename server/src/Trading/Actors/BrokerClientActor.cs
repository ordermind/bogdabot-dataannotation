using Akka.Actor;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.GetCurrentPriceProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.StartBrokerClientProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols.StopBrokerClientProtocol;

namespace Bogdabot.Server.Trading.Actors;

class BrokerClientActor(
    ILogger<BrokerClientActor> logger,
    BrokerClientConfiguration config
) : ReceiveActor, IWithUnboundedStash
{
    public IStash Stash { get; set; } = null!;
    private ILogger<BrokerClientActor> Logger { get; } = logger;
    protected BrokerClientConfiguration Config { get; } = config;
    protected BrokerClientIdentifier Identifier { get; } = new BrokerClientIdentifier
    {
        UserGuid = config.UserGuid,
        BrokerName = config.BrokerName,
    };

    protected Dictionary<CurrencySymbol, NonNegativeDecimalNumber> CurrentPrices { get; } = [];

    #region External messages
    protected record TickerUpdateMessage
    {
        public required CurrencySymbol AssetName { get; init; }
        public required NonNegativeDecimalNumber Price { get; init; }
    }
    #endregion

    #region Internal messages
    protected record InitializeInternalMessage { }
    protected record StartInternalMessage { }

    #endregion

    protected override void PreStart()
    {
        base.PreStart();

        Become(PhaseInitializing);

        Self.Tell(new InitializeInternalMessage());
    }

    protected void PhaseInitializing()
    {
        ReceiveAsync<InitializeInternalMessage>(HandleInitialize);

        ReceiveAny(_ => Stash.Stash());
    }

    protected async Task HandleInitialize(InitializeInternalMessage _)
    {
        var result = await Initialize();

        if (result is Result<bool>.Error error)
        {
            Context.Parent.Tell(new BrokerClientActorNotStartedResponse
            {
                Identifier = Identifier,
                Config = Config,
                Error = error.ErrorValue
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseStarting);

        Self.Tell(new StartInternalMessage { });
    }

    protected async Task<Result<bool>> Initialize()
    {
        try
        {
            // TODO: Create connection with broker

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new BrokerClientInitializationError(e.ToString()));
        }
    }

    protected void PhaseStarting()
    {
        ReceiveAsync<StartInternalMessage>(HandleStart);

        ReceiveAny(_ => Stash.Stash());
    }

    protected async Task HandleStart(StartInternalMessage _)
    {
        var result = Start();

        if (result is Result<bool>.Error error)
        {
            Context.Parent.Tell(new BrokerClientActorNotStartedResponse
            {
                Identifier = Identifier,
                Config = Config,
                Error = error.ErrorValue
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseRunning);

        Context.Parent.Tell(
            new BrokerClientActorStartedResponse
            {
                Identifier = Identifier
            }
        );

        Stash.UnstashAll();
    }

    protected Result<bool> Start()
    {
        try
        {
            // TODO: possible other actions that need to be done to run

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new BrokerClientStartInternalError(e.ToString()));
        }
    }

    protected void PhaseRunning()
    {
        Receive<TickerUpdateMessage>(HandleTickerUpdateMessage);
        Receive<GetCurrentPriceRequest>(HandleGetCurrentPriceRequest);
        Receive<StopBrokerClientActorRequest>(HandleStop);
    }

    protected void HandleTickerUpdateMessage(TickerUpdateMessage message)
    {
        CurrentPrices[message.AssetName] = message.Price;
    }

    protected void HandleGetCurrentPriceRequest(GetCurrentPriceRequest request)
    {
        if (CurrentPrices.TryGetValue(request.BaseCurrency, out var currentPrice))
        {
            Sender.Tell(new GetCurrentPriceResponse
            {
                BaseCurrency = request.BaseCurrency,
                Result = new Result<NonNegativeDecimalNumber>.Ok(currentPrice)
            });
        }
        else
        {
            // Temporary test response that returns 0 euro for unknown currencies
            Sender.Tell(new GetCurrentPriceResponse
            {
                BaseCurrency = request.BaseCurrency,
                Result = new Result<NonNegativeDecimalNumber>.Ok(0m)
            });

            // Sender.Tell(new GetCurrentPriceResponse
            // {
            //     BaseCurrency = request.BaseCurrency,
            //     Result = new Result<NonNegativeDecimalNumber>.Error(new CurrentPriceNotFoundError($"The current price for the asset {request.BaseCurrency} could not be found.")),
            // });
        }
    }

    protected void HandleStop(StopBrokerClientActorRequest _)
    {
        var result = Stop();

        if (result is Result<bool>.Error error)
        {
            Context.Parent.Tell(
                new BrokerClientActorNotStoppedResponse
                {
                    Identifier = Identifier,
                    Error = error.ErrorValue,
                }
            );

            return;
        }

        Context.Parent.Tell(
            new BrokerClientActorStoppedResponse
            {
                Identifier = Identifier
            }
        );
    }

    protected Result<bool> Stop()
    {
        try
        {
            Context.Stop(Self);

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new BrokerClientStopInternalError(e.ToString()));
        }
    }
}