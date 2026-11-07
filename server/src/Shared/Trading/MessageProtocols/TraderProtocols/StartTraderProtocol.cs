using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.Trading.Config;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols;

public static class StartTraderProtocol
{
    #region Starting a trader
    public record StartNewTraderRequest
    {
        public required TraderConfiguration Config { get; init; }
    }

    public record StartActiveTraderRequest
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public abstract record StartTraderResponse { }

    public record NewTraderNotStartedResponse : StartTraderResponse
    {
        public required TraderConfiguration Config { get; init; }
        public required AbstractError Error { get; init; }
    }

    public abstract record StartExistingTraderResponse : StartTraderResponse
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public record ExistingTraderStartedResponse : StartExistingTraderResponse { }

    public record ExistingTraderNotStartedResponse : StartExistingTraderResponse
    {
        public required AbstractError Error { get; init; }
    }
    #endregion

    #region Creating and starting the trader actor internally

    public record StartTraderActorRequest
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
        public required TraderConfiguration Config { get; init; }
    }

    public abstract record StartTraderActorResponse
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
        public required TraderConfiguration Config { get; init; }
    }

    public record TraderActorStartedResponse : StartTraderActorResponse { }

    public record TraderActorNotStartedResponse : StartTraderActorResponse
    {
        public required AbstractError Error { get; init; }
    }

    #endregion
}