using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Web.Api.V1.Controllers;
using Bogdabot.Server.Shared.Trading.Config;

namespace Bogdabot.Server.Web.Api.V1.Transformers;

public class WebTransformer
{
    public TraderConfiguration TransformCreateTraderFormDataToTraderConfiguration(CreateTraderFormData formData, ulong currentUserId, Guid currentUserGuid)
    {
        return new TraderConfiguration
        {
            UserId = currentUserId,
            UserGuid = currentUserGuid,
            BrokerName = Enum.Parse<BrokerEnum>(formData.BrokerName ?? ""),
            StrategyName = Enum.Parse<TradingStrategyEnum>(formData.StrategyName ?? ""),
            BaseCurrency = new CurrencySymbol(formData.Base ?? ""),
            QuoteCurrency = new CurrencySymbol(formData.Quote ?? ""),
            InitialBaseInvestment = new NonNegativeDecimalNumber(formData.InitialBaseInvestment ?? 0m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(formData.InitialQuoteInvestment ?? 0m),
        };
    }
}
