using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Trading.MessageProtocols.BrokerClientProtocols;

public static class GetCurrentPriceProtocol
{
    public record GetCurrentPriceRequest
    {
        public required CurrencySymbol BaseCurrency { get; init; }
    }

    public record GetCurrentPriceResponse
    {
        public required CurrencySymbol BaseCurrency { get; init; }
        public required Result<NonNegativeDecimalNumber> Result { get; init; }
    }
}