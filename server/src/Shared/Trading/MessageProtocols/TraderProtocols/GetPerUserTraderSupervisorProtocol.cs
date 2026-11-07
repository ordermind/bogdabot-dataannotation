using Akka.Actor;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols;

public static class GetPerUserTraderSupervisorProtocol
{
    public record GetTraderSupervisorRequest
    {
        public required NonEmptyGuid UserGuid { get; init; }
    }

    public abstract record GetTraderSupervisorResponse { }

    public record TraderSupervisorFoundResponse : GetTraderSupervisorResponse
    {
        public required IActorRef TraderSupervisorReference { get; init; }
    }

    public record TraderSupervisorNotFoundResponse : GetTraderSupervisorResponse
    {
        public required string ActorId { get; init; }
    }
}