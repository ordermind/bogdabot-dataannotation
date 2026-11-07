using Akka.Actor;
using Akka.DependencyInjection;
using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Trading.MessageProtocols;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.GetPerUserTraderSupervisorProtocol;

namespace Bogdabot.Server.Infrastructure.Akka;

public class ActorHostService(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime appLifetime,
    IConfiguration configuration
) : IHostedService, IAkkaHost
{
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private volatile ActorSystem? actorSystem;
    private Task? pendingTermination;
    private readonly IConfiguration configuration = configuration;
    private readonly IServiceProvider serviceProvider = serviceProvider;

    private readonly IHostApplicationLifetime applicationLifetime = appLifetime;

    private volatile Task<IActorRef>? tradingRootSupervisorReady;

    public IConfiguration Configuration => configuration;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (actorSystem is not null)
            {
                return;
            }

            if (pendingTermination is not null)
            {
                await pendingTermination.WaitAsync(cancellationToken);
                pendingTermination = null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var bootstrap = BootstrapSetup.Create();

            // Enable DI support inside this ActorSystem, if needed.
            var diSetup = DependencyResolverSetup.Create(serviceProvider);

            var actorSystemSetup = bootstrap.And(diSetup);
            var system = ActorSystem.Create("akka-universe", actorSystemSetup);

            try
            {
                var resolver = DependencyResolver.For(system);
                var tradingRootSupervisorRef = system.ActorOf(
                    resolver.Props<TradingRootSupervisor>(resolver),
                    "trader-root-supervisor"
                );

                tradingRootSupervisorReady = Task.FromResult(tradingRootSupervisorRef);
                actorSystem = system;
                _ = ObserveTerminationAsync(system);
            }
            catch
            {
                await system.Terminate();
                throw;
            }
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var system = actorSystem;
            if (system is null)
            {
                if (pendingTermination is not null)
                {
                    await pendingTermination.WaitAsync(cancellationToken);
                    pendingTermination = null;
                }

                return;
            }

            tradingRootSupervisorReady = null;
            actorSystem = null;
            pendingTermination = system.Terminate();
            await pendingTermination.WaitAsync(cancellationToken);
            pendingTermination = null;
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private async Task ObserveTerminationAsync(ActorSystem system)
    {
        await system.WhenTerminated;

        await lifecycleLock.WaitAsync();
        try
        {
            if (ReferenceEquals(actorSystem, system))
            {
                actorSystem = null;
                tradingRootSupervisorReady = null;
                applicationLifetime.StopApplication();
            }
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task<IActorRef> GetTradingRootSupervisor()
    {
        var ready = tradingRootSupervisorReady
            ?? throw new InvalidOperationException("The Akka actor system is not running.");
        return await ready;
    }

    public async Task<IActorRef> GetTraderSupervisorForUser(NonEmptyGuid userGuid)
    {
        var traderSupervisor = await GetTradingRootSupervisor();
        // Ask the trader supervisor to return or create the per-user supervisor.

        var result = await traderSupervisor.Ask<GetTraderSupervisorResponse>(new GetTraderSupervisorRequest { UserGuid = userGuid });

        return result switch
        {
            TraderSupervisorFoundResponse successResponse => successResponse.TraderSupervisorReference,
            TraderSupervisorNotFoundResponse => throw new ActorNotFoundException($"The trader supervisor for user {userGuid} could not be found."),
            _ => throw new Exception("Unknown response type")
        };
    }
}
