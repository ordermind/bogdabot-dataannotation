using System.ComponentModel.DataAnnotations;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Web.Api.V1.ValidationAttributes;

namespace Bogdabot.Server.Web.Api.V1.Controllers;

public record CreateTraderFormData
{
    /// <example>IBKR</example>
    [Required]
    [Enum<BrokerEnum>]
    public string? BrokerName { get; init; }

    /// <example>SpotGrid</example>
    [Required]
    [Enum<TradingStrategyEnum>]
    public string? StrategyName
    { get; init; }

    /// <summary>
    /// The security to buy or sell, also known as base currency.
    /// </summary>
    /// <example>BTC</example>
    [Required]
    [StringLength(100)]
    public string? Base { get; init; }

    /// <summary>
    /// The currency for the payment, also known as quote currency.
    /// </summary>
    /// <example>EUR</example>
    [Required]
    [StringLength(100)]
    public string? Quote { get; init; }

    /// <example>50.25</example>
    [Required]
    [NonNegativeNumber]
    public decimal? InitialBaseInvestment { get; init; }

    /// <example>50.25</example>
    [Required]
    [NonNegativeNumber]
    public decimal? InitialQuoteInvestment { get; init; }
}
