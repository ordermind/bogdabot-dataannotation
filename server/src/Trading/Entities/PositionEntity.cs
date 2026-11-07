using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Trading.Entities;

public class PositionEntity()
{
    public ulong? Id { get; set; }

    /// <summary>
    /// The security to buy or sell, also known as base currency.
    /// </summary>
    public required string Base { get; set; }

    /// <summary>
    /// The currency for the payment, also known as quote currency.
    /// </summary>
    public required string Quote { get; set; }

    public required PositiveDecimalNumber Quantity { get; set; }

    public required PositiveDecimalNumber DesiredProfitMargin { get; set; }

    public required string BuyOrderExternalId { get; set; }

    public required DateTimeOffset BoughtAt { get; set; }

    public required PositiveDecimalNumber BuyPrice { get; set; }

    public required PositiveDecimalNumber BuyFee { get; set; }

    public string? SellOrderExternalId { get; set; }

    public DateTimeOffset? SoldAt { get; set; }

    public PositiveDecimalNumber? SellPrice { get; set; }

    public PositiveDecimalNumber? SellFee { get; set; }

    public decimal? RealizedResult { get; set; }

    public decimal? RealizedProfitMargin { get; set; }
}
