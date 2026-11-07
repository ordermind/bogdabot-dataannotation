using Bogdabot.Server.Trading.DataClasses;

namespace Bogdabot.Server.Trading.TradingStrategies.Configuration;

class SpotGridConfig(
    decimal lowerPrice,
    decimal upperPrice,
    int numberOfGrids,
    decimal baseAssetInvestment,
    decimal quoteAssetInvestment,
    AssetPair assetPair)
{
    public decimal LowerPrice { get; } = lowerPrice;
    public decimal UpperPrice { get; } = upperPrice;
    public int NumberOfGrids { get; } = numberOfGrids;
    public decimal BaseAssetInvestment { get; } = baseAssetInvestment;
    public decimal QuoteAssetInvestment { get; } = quoteAssetInvestment;
    public AssetPair AssetPair { get; } = assetPair;
}
