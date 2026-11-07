using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bogdabot.Server.Trading.Entities;

public enum TraderStatusEnum { Created, Running, Failed }
public class TraderEntity()
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public ulong Id { get; set; }

    public NonEmptyGuid Guid { get; set; }

    public required ulong UserId { get; set; }

    public required NonEmptyGuid UserGuid { get; set; }

    public TraderStatusEnum Status { get; set; } = TraderStatusEnum.Created;

    public required BrokerEnum BrokerName { get; set; }

    public required TradingStrategyEnum StrategyName
    { get; set; }

    /// <summary>
    /// The security to buy or sell.
    /// </summary>
    public required CurrencySymbol BaseCurrency { get; set; }

    public required NonNegativeDecimalNumber InitialPrice { get; set; }

    public required NonNegativeDecimalNumber InitialBaseBalance { get; set; }

    /// <summary>
    /// The currency for the payment.
    /// </summary>
    public required CurrencySymbol QuoteCurrency { get; set; }

    public required NonNegativeDecimalNumber InitialQuoteBalance { get; set; }

    /// <summary>
    /// The initial sum value of base and quote assets, expressed in the quote currency.
    /// </summary>
    public required NonNegativeDecimalNumber InitialTotalValue { get; set; }

    public required DateTimeOffset FirstStartedAt { get; set; }
    public required DateTimeOffset LastStartedAt { get; set; }
}
