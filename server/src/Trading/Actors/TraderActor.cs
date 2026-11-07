using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Trading.Actors;

class TraderActor : ReceiveActor, IWithUnboundedStash
{
    public IStash Stash { get; set; } = null!;

    protected ITraderDatabaseConfigFactory DbConfigFactory { get; }
    protected ITraderDbMigrationService MigrationService { get; }
    protected TraderConfiguration TraderConfig { get; }
    protected TraderIdentifier TraderIdentifier { get; }
    protected IDatabaseConfig DatabaseConfig { get; }
    protected ILogger<TraderActor> Logger { get; }

    #region Internal messages
    protected record InitializeInternalMessage { }
    protected record StartInternalMessage { }

    #endregion

    public TraderActor(
        ITraderDatabaseConfigFactory dbConfigFactory,
        ITraderDbMigrationService migrationService,
        TraderConfiguration traderConfig,
        TraderIdentifier traderIdentifier,
        ILogger<TraderActor> logger
    )
    {
        DbConfigFactory = dbConfigFactory;
        MigrationService = migrationService;
        TraderConfig = traderConfig;
        TraderIdentifier = traderIdentifier;
        Logger = logger;

        DatabaseConfig = DbConfigFactory.Create(
            userGuid: TraderConfig.UserGuid,
            actorId: TraderActorId.FromTraderGuid(TraderIdentifier.TraderGuid));
    }

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
            Context.Parent.Tell(new TraderActorNotStartedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Config = TraderConfig,
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
            // TODO: Create a strategy actor and possibly also a broker client listener actor that subscribes to certain broker client messages and passes them on to this actor. Also possibly create a persistence actor to handle persisting data.

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new TraderInitializationError(e.ToString()));
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
            Context.Parent.Tell(new TraderActorNotStartedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Config = TraderConfig,
                Error = error.ErrorValue
            });

            Context.Stop(Self);

            return;
        }

        Become(PhaseRunning);

        Context.Parent.Tell(
            new TraderActorStartedResponse
            {
                TraderIdentifier = TraderIdentifier,
                Config = TraderConfig
            }
        );

        Stash.UnstashAll();
    }

    protected Result<bool> Start()
    {
        try
        {
            // TODO: this.tradingStrategy.init();

            return new Result<bool>.Ok(true);
        }
        catch (Exception e)
        {
            return new Result<bool>.Error(new TraderStartInternalError(e.ToString()));
        }
    }

    protected void PhaseRunning()
    {
        Receive<StopTraderActorRequest>(HandleStop);
    }

    protected void HandleStop(StopTraderActorRequest request)
    {
        if (request.TraderIdentifier != TraderIdentifier)
        {
            Logger.LogError("A request for stopping a trader actor was sent to the wrong trader actor. My identifier: {myIdentifier}. Identifier in the request: {theirIdentifier}", TraderIdentifier, request.TraderIdentifier);

            Sender.Tell(new TraderActorNotStoppedResponse
            {
                TraderIdentifier = request.TraderIdentifier,
                Error = new IllegalActorMessageError($"This request was sent to the wrong trader actor. My identifier: {TraderIdentifier}. Identifier in the request: {request.TraderIdentifier}")
            });

            return;
        }

        var result = Stop();

        if (result is Result<bool>.Error error)
        {
            Context.Parent.Tell(
                new TraderActorNotStoppedResponse
                {
                    TraderIdentifier = TraderIdentifier,
                    Error = error.ErrorValue,
                }
            );

            return;
        }

        Context.Parent.Tell(
            new TraderActorStoppedResponse
            {
                TraderIdentifier = TraderIdentifier
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
            return new Result<bool>.Error(new TraderStopInternalError(e.ToString()));
        }
    }
}
