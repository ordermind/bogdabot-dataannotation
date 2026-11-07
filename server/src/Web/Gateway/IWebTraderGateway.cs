using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Web.Gateway;

public interface IWebTraderGateway
{
    Task<Result<NonEmptyGuid>> CreateTrader(TraderConfiguration traderConfig);

    Task<Result<NonEmptyGuid>> StartActiveTrader(TraderIdentifier traderIdentifier);

    Task<Result<NonEmptyGuid>> StopOneTrader(TraderIdentifier traderIdentifier);
    Task<Result<NonEmptyGuid>> StopAllTradersForUser(NonEmptyGuid userGuid);
    Task<Result<NonEmptyGuid>> DeleteTrader(TraderIdentifier traderIdentifier);
}
