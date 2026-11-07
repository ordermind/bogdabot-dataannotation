using Bogdabot.Server.Shared.Errors;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols;

public static class DeleteTraderProtocol
{
    public record DeleteTraderRequest
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public abstract record DeleteTraderResponse
    {
        public required TraderIdentifier TraderIdentifier { get; init; }
    }

    public record TraderDeletedResponse : DeleteTraderResponse { }

    public record TraderNotDeletedResponse : DeleteTraderResponse
    {
        public required AbstractError Error { get; init; }
    }
}