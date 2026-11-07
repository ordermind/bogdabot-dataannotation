using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;

namespace Bogdabot.Server.Shared.Trading.Config;

public record TraderConfiguration
{
    public required ulong UserId { get; init; }
    public required NonEmptyGuid UserGuid { get; init; }
    public required BrokerEnum BrokerName { get; init; }
    public required TradingStrategyEnum StrategyName
    { get; init; }
    public required CurrencySymbol BaseCurrency { get; init; }
    public required CurrencySymbol QuoteCurrency { get; init; }
    public required NonNegativeDecimalNumber InitialBaseInvestment { get; init; }
    public required NonNegativeDecimalNumber InitialQuoteInvestment { get; init; }
}