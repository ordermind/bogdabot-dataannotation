namespace Bogdabot.Server.Trading.DataClasses;

class AssetPair(string baseAsset, string quoteAsset)
{
    public string BaseAsset { get; } = baseAsset;
    public string QuoteAsset { get; } = quoteAsset;
}
