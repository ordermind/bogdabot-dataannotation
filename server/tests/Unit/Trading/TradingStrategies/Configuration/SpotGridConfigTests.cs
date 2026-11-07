using Bogdabot.Server.Trading.DataClasses;
using Bogdabot.Server.Trading.TradingStrategies.Configuration;

namespace Bogdabot.Server.Tests.Unit.Trading.TradingStrategies.Configuration;

public class SpotGridConfigTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var pair = new AssetPair("BTC", "EUR");
        var config = new SpotGridConfig(100m, 200m, 10, 1m, 1000m, pair);

        Assert.Equal(100m, config.LowerPrice);
        Assert.Equal(200m, config.UpperPrice);
        Assert.Equal(10, config.NumberOfGrids);
        Assert.Equal(1m, config.BaseAssetInvestment);
        Assert.Equal(1000m, config.QuoteAssetInvestment);
        Assert.Same(pair, config.AssetPair);
    }
}
