using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;

namespace Bogdabot.Server.Trading.Repositories;

public interface IStoppedTraderRepository
{
    Task<IEnumerable<StoppedTraderEntity>> GetAllForUser(NonEmptyGuid userGuid);
    Task<IEnumerable<StoppedTraderEntity>> GetAllForUser(NonEmptyGuid userGuid, StoppedTraderDbContext context);

    Task<StoppedTraderEntity?> GetOneByGuid(NonEmptyGuid traderGuid);
    Task<StoppedTraderEntity?> GetOneByGuid(NonEmptyGuid traderGuid, StoppedTraderDbContext context);

    Task<StoppedTraderEntity> SaveTraderAsStopped(
        TraderEntity trader,
        NonNegativeDecimalNumber finalPrice,
        NonNegativeDecimalNumber finalBaseBalance,
        NonNegativeDecimalNumber finalQuoteBalance,
        decimal realizedResult
    );
    Task<StoppedTraderEntity> SaveTraderAsStopped(
        TraderEntity trader,
        NonNegativeDecimalNumber finalPrice,
        NonNegativeDecimalNumber finalBaseBalance,
        NonNegativeDecimalNumber finalQuoteBalance,
        decimal realizedResult,
        StoppedTraderDbContext context
    );

    Task<StoppedTraderEntity> Save(StoppedTraderEntity stoppedTrader);
    Task<StoppedTraderEntity> Save(StoppedTraderEntity stoppedTrader, StoppedTraderDbContext context);

    Task DeleteByGuid(NonEmptyGuid traderGuid);
    Task DeleteByGuid(NonEmptyGuid traderGuid, StoppedTraderDbContext context);
}
