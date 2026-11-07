using Bogdabot.Server.Trading.DataClasses;

namespace Bogdabot.Server.Tests.Unit.Trading.DataClasses;

public class AssetPairTests
{
    [Fact]
    public void Constructor_SetsAssets()
    {
        var pair = new AssetPair("BTC", "EUR");

        Assert.Equal("BTC", pair.BaseAsset);
        Assert.Equal("EUR", pair.QuoteAsset);
    }
}
