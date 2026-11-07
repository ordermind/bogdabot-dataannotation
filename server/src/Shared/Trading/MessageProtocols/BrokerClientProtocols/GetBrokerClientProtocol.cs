using Akka.Actor;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols;

public static class GetBrokerClientProtocol
{
    public record GetBrokerClientActorRequest
    {
        public required BrokerClientIdentifier Identifier { get; init; }
    }

    public record GetBrokerClientActorResponse
    {
        public required Result<IActorRef> Result { get; init; }
    }
}