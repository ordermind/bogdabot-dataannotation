using Akka.Actor;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols;

public static class GetTraderProtocol
{
    public record GetTraderActorRequest
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public record GetTraderActorResponse
    {
        public required Result<IActorRef> Result { get; init; }
    }
}