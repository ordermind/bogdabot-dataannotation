using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;

namespace Bogdabot.Server.Trading.Repositories;

public interface ITraderRepository
{
    Task<IEnumerable<TraderEntity>> GetActiveTradersForUser(NonEmptyGuid userGuid);
    Task<IEnumerable<TraderEntity>> GetActiveTradersForUser(NonEmptyGuid userGuid, TraderDbContext context);

    Task<TraderEntity> CreateAndSaveTrader(TraderConfiguration config, NonNegativeDecimalNumber initialPrice);
    Task<TraderEntity> CreateAndSaveTrader(TraderConfiguration config, NonNegativeDecimalNumber initialPrice, TraderDbContext context);

    Task<TraderEntity?> GetOneByGuid(NonEmptyGuid traderGuid);
    Task<TraderEntity?> GetOneByGuid(NonEmptyGuid traderGuid, TraderDbContext context);

    Task<TraderEntity> Save(TraderEntity trader);
    Task<TraderEntity> Save(TraderEntity trader, TraderDbContext context);

    Task DeleteByGuid(NonEmptyGuid traderGuid);
    Task DeleteByGuid(NonEmptyGuid traderGuid, TraderDbContext context);
}
