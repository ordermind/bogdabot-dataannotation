using Bogdabot.Server.Shared.Errors;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols;

public static class StopBrokerClientProtocol
{
    public record StopBrokerClientActorRequest
    {
        public required BrokerClientIdentifier Identifier { get; init; }
    }

    public abstract record StopBrokerClientActorResponse
    {
        public required BrokerClientIdentifier Identifier { get; init; }
    }

    public record BrokerClientActorStoppedResponse : StopBrokerClientActorResponse { }

    public record BrokerClientActorNotStoppedResponse : StopBrokerClientActorResponse
    {
        public required AbstractError Error { get; init; }
    }
}
