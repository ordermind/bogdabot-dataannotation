using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;

namespace Bogdabot.Server.Trading.StateManagement;

public interface ITraderLifecycleService
{
    Task<IEnumerable<TraderIdentifier>> GetActiveTradersForUser(NonEmptyGuid userGuid);

    Task<NonEmptyGuid> CreateTrader(TraderConfiguration config, NonNegativeDecimalNumber initialPrice);

    Task MarkTraderRunning(NonEmptyGuid traderGuid);

    Task MarkTraderFailed(NonEmptyGuid traderGuid);

    Task<TraderEntity> LoadTrader(NonEmptyGuid traderGuid);

    Task<TraderConfiguration> GetTraderConfiguration(NonEmptyGuid traderGuid);

    Task<TraderStatusEnum> GetCurrentStatus(NonEmptyGuid traderGuid);

    Task PersistStoppedTrader(
        NonEmptyGuid traderGuid,
        NonNegativeDecimalNumber finalPrice,
        NonNegativeDecimalNumber finalBaseBalance,
        NonNegativeDecimalNumber finalQuoteBalance,
        decimal realizedResult
    );

    Task DeleteTraderByGuid(NonEmptyGuid traderGuid);

    void DeleteTraderDatabase(IDatabaseConfig databaseConfig);
}
