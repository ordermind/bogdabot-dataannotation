using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;

namespace Bogdabot.Server.Tests.Unit.Trading.Actors;

public class TraderActorConfigurationTests
{
    [Fact]
    public void InitProperties_AreAssignedCorrectly()
    {
        var guid = NonEmptyGuid.FromValue(Guid.NewGuid());
        var traderGuid = NonEmptyGuid.FromValue(Guid.NewGuid());

        var config = new TraderConfiguration
        {
            UserId = 7,
            UserGuid = guid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            QuoteCurrency = new CurrencySymbol("USD"),
            InitialBaseInvestment = new NonNegativeDecimalNumber(123m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(321m)
        };

        Assert.Equal((ulong)7, config.UserId);
        Assert.Equal(guid, config.UserGuid);
        Assert.Equal(BrokerEnum.IBKR, config.BrokerName);
        Assert.Equal(TradingStrategyEnum.SpotGrid, config.StrategyName);
        Assert.Equal("BTC", config.BaseCurrency.Value);
        Assert.Equal("USD", config.QuoteCurrency.Value);
        Assert.Equal(123m, config.InitialBaseInvestment.Value);
        Assert.Equal(321m, config.InitialQuoteInvestment.Value);
    }
}
