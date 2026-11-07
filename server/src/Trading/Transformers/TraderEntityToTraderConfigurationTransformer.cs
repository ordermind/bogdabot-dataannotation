using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Trading.Entities;

namespace Bogdabot.Server.Trading.Transformers;

public class TraderEntityToTraderConfigurationTransformer
{
    public TraderConfiguration transformActiveTrader(TraderEntity traderEntity)
    {
        return new TraderConfiguration
        {
            UserId = traderEntity.UserId,
            UserGuid = traderEntity.UserGuid,
            BrokerName = traderEntity.BrokerName,
            StrategyName = traderEntity.StrategyName,
            BaseCurrency = traderEntity.BaseCurrency,
            QuoteCurrency = traderEntity.QuoteCurrency,
            InitialBaseInvestment = traderEntity.InitialBaseBalance,
            InitialQuoteInvestment = traderEntity.InitialQuoteBalance
        };
    }

    /// <summary>
    /// This method is meant to help recreate a stopped trader. This is why the final base balance and quote balance are used to initiate the new trader.
    /// </summary>
    /// <param name="stoppedTraderEntity"></param>
    /// <returns></returns>
    public TraderConfiguration transformStoppedTrader(StoppedTraderEntity stoppedTraderEntity)
    {
        return new TraderConfiguration
        {
            UserId = stoppedTraderEntity.UserId,
            UserGuid = stoppedTraderEntity.UserGuid,
            BrokerName = stoppedTraderEntity.BrokerName,
            StrategyName = stoppedTraderEntity.StrategyName,
            BaseCurrency = stoppedTraderEntity.BaseCurrency,
            QuoteCurrency = stoppedTraderEntity.QuoteCurrency,
            InitialBaseInvestment = stoppedTraderEntity.FinalBaseBalance,
            InitialQuoteInvestment = stoppedTraderEntity.FinalQuoteBalance
        };
    }
}