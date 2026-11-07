using Bogdabot.Server.Shared.Errors;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols;

public static class StopTraderProtocol
{
    #region Stopping traders from the outside
    public record StopTraderRequest
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public abstract record StopTraderResponse
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public record TraderStoppedResponse : StopTraderResponse { }

    public record TraderNotStoppedResponse : StopTraderResponse
    {
        public required AbstractError Error { get; init; }
    }

    #endregion

    #region Stopping traders internally
    public record StopTraderActorRequest
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public abstract record StopTraderActorResponse
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public record TraderActorStoppedResponse : StopTraderActorResponse { }

    public record TraderActorNotStoppedResponse : StopTraderActorResponse
    {
        public required AbstractError Error { get; init; }
    }

    #endregion
}
