using Akka.Actor;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Actors;

public interface IAkkaHost
{
    public Task StartAsync(CancellationToken cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken);

    public Task<IActorRef> GetTradingRootSupervisor();

    public Task<IActorRef> GetTraderSupervisorForUser(NonEmptyGuid userGuid);
}
