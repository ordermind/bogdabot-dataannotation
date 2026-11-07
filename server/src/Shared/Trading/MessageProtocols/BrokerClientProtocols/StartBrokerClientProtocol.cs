using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading.Config;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols;

public static class StartBrokerClientProtocol
{
    public record StartBrokerClientActorRequest
    {
        public required BrokerClientConfiguration Config { get; init; }
    }

    public abstract record StartBrokerClientActorResponse
    {
        public required BrokerClientIdentifier Identifier { get; init; }
    }

    public record BrokerClientActorStartedResponse : StartBrokerClientActorResponse { }

    public record BrokerClientActorNotStartedResponse : StartBrokerClientActorResponse
    {
        public required BrokerClientConfiguration Config { get; init; }
        public required AbstractError Error { get; init; }
    }
}