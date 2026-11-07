using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bogdabot.Server.Trading.Entities;

public class StoppedTraderEntity()
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public ulong Id { get; set; }

    public NonEmptyGuid Guid { get; set; }

    public required ulong UserId { get; set; }

    public required NonEmptyGuid UserGuid { get; set; }

    public required BrokerEnum BrokerName { get; set; }

    public required TradingStrategyEnum StrategyName
    { get; set; }

    /// <summary>
    /// The security to buy or sell.
    /// </summary>
    public required CurrencySymbol BaseCurrency { get; set; }

    public required NonNegativeDecimalNumber InitialPrice { get; set; }

    public required NonNegativeDecimalNumber InitialBaseBalance { get; set; }

    public required NonNegativeDecimalNumber FinalPrice { get; set; }

    public required NonNegativeDecimalNumber FinalBaseBalance { get; set; }

    /// <summary>
    /// The currency for the payment.
    /// </summary>
    public required CurrencySymbol QuoteCurrency { get; set; }

    public required NonNegativeDecimalNumber InitialQuoteBalance { get; set; }

    public required NonNegativeDecimalNumber FinalQuoteBalance { get; set; }

    /// <summary>
    /// The initial sum value of base and quote assets, expressed in the quote currency.
    /// </summary>
    public required NonNegativeDecimalNumber InitialTotalValue { get; set; }

    /// <summary>
    /// The sum value of base and quote assets at the time of stopping the trader, expressed in the quote currency.
    /// </summary>
    public required NonNegativeDecimalNumber FinalTotalValue { get; set; }

    /// <summary>
    /// The unrealized result is the difference between the initial total value and the final total value.
    /// </summary>
    public required decimal UnrealizedResult { get; set; }

    /// <summary>
    /// The realized result is only updated when a sell has been executed. Depending on the strategy, the result is calculated based on the difference between the price at buy time and the price at sell time of a position.
    /// </summary>
    public required decimal RealizedResult { get; set; }

    /// <summary>
    /// The total result is the sum of the realized result and the unrealized result.
    /// </summary>
    public required decimal TotalResult { get; set; }

    public required decimal UnrealizedProfitMargin { get; set; }

    public required decimal RealizedProfitMargin { get; set; }

    public required decimal TotalProfitMargin { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    public required DateTimeOffset StoppedAt { get; set; }
}
