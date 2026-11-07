using Bogdabot.Server.Trading.DataClasses;

namespace Bogdabot.Server.Trading.Entities.SpotGridStrategy;

class ArbitrageEntity(
    AssetPair assetPair,
    ulong? buyOrderId,
    decimal buyQuantity,
    decimal buyPrice,
    decimal buyFee,
    decimal buySumExclFee,
    decimal buySumInclFee,
    ulong buyCompletedAt,
    ulong sellOrderId,
    decimal sellQuantity,
    decimal sellPrice,
    decimal sellFee,
    decimal sellSumExclFee,
    decimal sellSumInclFee,
    ulong sellCompletedAt,
    decimal profitBeforeFees,
    decimal profitAfterFees,
    ulong completedAt)
{
    public AssetPair AssetPair { get; set; } = assetPair;
    public ulong? BuyOrderId { get; set; } = buyOrderId;
    public decimal BuyQuantity { get; set; } = buyQuantity;
    public decimal BuyPrice { get; set; } = buyPrice;
    public decimal BuyFee { get; set; } = buyFee;
    public decimal BuySumExclFee { get; set; } = buySumExclFee;
    public decimal BuySumInclFee { get; set; } = buySumInclFee;
    public ulong BuyCompletedAt { get; set; } = buyCompletedAt;
    public ulong SellOrderId { get; set; } = sellOrderId;
    public decimal SellQuantity { get; set; } = sellQuantity;
    public decimal SellPrice { get; set; } = sellPrice;
    public decimal SellFee { get; set; } = sellFee;
    public decimal SellSumExclFee { get; set; } = sellSumExclFee;
    public decimal SellSumInclFee { get; set; } = sellSumInclFee;
    public ulong SellCompletedAt { get; set; } = sellCompletedAt;
    public decimal ProfitBeforeFees { get; set; } = profitBeforeFees;
    public decimal ProfitAfterFees { get; set; } = profitAfterFees;
    public ulong CompletedAt { get; set; } = completedAt;
}
