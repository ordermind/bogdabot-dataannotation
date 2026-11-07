namespace Bogdabot.Server.Trading.TradingStrategies;

interface ITradingStrategy
{
    public void init();
    public void tick();
}
