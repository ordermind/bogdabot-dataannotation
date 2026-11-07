using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols;

/// <summary>
/// This protocol should only be used by the trader root supervisor in order to shut down all traders for a user.
/// </summary>
public static class StopAllTradersForUserProtocol
{
    public record StopAllTradersRequest
    {
        public required NonEmptyGuid UserGuid { get; init; }
    }

    public abstract record StopAllTradersResponse
    {
        public required NonEmptyGuid UserGuid { get; init; }
    }

    public record AllTradersStoppedResponse : StopAllTradersResponse { }

    public record AllTradersNotStoppedResponse : StopAllTradersResponse
    {
        public required AbstractError Error { get; init; }
    }

}
