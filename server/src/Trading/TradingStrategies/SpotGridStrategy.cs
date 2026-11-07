using Bogdabot.Server.Trading.BrokerClients;

namespace Bogdabot.Server.Trading.TradingStrategies;

class SpotGridStrategy(IBrokerClient exchangeClient) : ITradingStrategy
{
    private IBrokerClient ExchangeClient { get; } = exchangeClient;

    public void init()
    {

    }

    public void tick()
    {

    }
}
