using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Web.Api.V1.Controllers;
using Bogdabot.Server.Web.Api.V1.Transformers;

namespace Bogdabot.Server.Tests.Unit.Web.Api.V1.Transformers;

public class WebTransformerTests
{
    [Fact]
    public void TransformCreateTraderFormDataToCreateTraderCommand_MapsAndParsesValues()
    {
        var formData = new CreateTraderFormData
        {
            BrokerName = "IBKR",
            StrategyName = "SpotGrid",
            Base = "BTC",
            Quote = "EUR",
            InitialBaseInvestment = 150.5m,
            InitialQuoteInvestment = 25.5m
        };

        var userGuid = NonEmptyGuid.FromValue(Guid.NewGuid());
        var sut = new WebTransformer();

        var command = sut.TransformCreateTraderFormDataToTraderConfiguration(formData, 42, userGuid);

        Assert.Equal((ulong)42, command.UserId);
        Assert.Equal(userGuid, command.UserGuid);
        Assert.Equal(BrokerEnum.IBKR, command.BrokerName);
        Assert.Equal(TradingStrategyEnum.SpotGrid, command.StrategyName);
        Assert.Equal("BTC", command.BaseCurrency.Value);
        Assert.Equal("EUR", command.QuoteCurrency.Value);
        Assert.Equal(150.5m, command.InitialBaseInvestment.Value);
        Assert.Equal(25.5m, command.InitialQuoteInvestment.Value);
    }
}
