using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Trading.Entities;
using SharedTraderRepository = Bogdabot.Server.Shared.Trading.ITraderRepository;
using TradingTraderRepository = Bogdabot.Server.Trading.Repositories.ITraderRepository;
using Microsoft.EntityFrameworkCore;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Account.Entities;

namespace Bogdabot.Server.Infrastructure.Postgres.Repositories;

public class TraderRepository(IDbContextFactory<TraderDbContext> contextFactory) : TradingTraderRepository, SharedTraderRepository
{
    private IDbContextFactory<TraderDbContext> ContextFactory { get; } = contextFactory;

    public async Task<IEnumerable<TraderEntity>> GetAllForUser(UserEntity userEntity)
    {
        return await GetAllForUser(userEntity.Id);
    }

    public async Task<IEnumerable<TraderEntity>> GetAllForUser(ulong userId)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        return await GetAllForUser(userId, context);
    }

    public async Task<IEnumerable<TraderEntity>> GetAllForUser(ulong userId, TraderDbContext context)
    {
        return await context.Traders
            .Where(trader => trader.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<TraderEntity>> GetAllForUser(NonEmptyGuid userGuid)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        return await GetAllForUser(userGuid, context);
    }

    public async Task<IEnumerable<TraderEntity>> GetAllForUser(NonEmptyGuid userGuid, TraderDbContext context)
    {
        return await context.Traders
            .Where(
                trader => trader.UserGuid == userGuid
            )
            .ToListAsync();
    }

    public async Task<TraderEntity?> GetOneByUserIdAndGuid(ulong userId, NonEmptyGuid traderGuid)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        return await GetOneByUserIdAndGuid(userId, traderGuid, context);
    }

    public async Task<TraderEntity?> GetOneByUserIdAndGuid(ulong userId, NonEmptyGuid traderGuid, TraderDbContext context)
    {
        return await context.Traders
            .FirstOrDefaultAsync(trader =>
                trader.UserId == userId
                && trader.Guid == traderGuid
            );
    }

    public async Task<TraderEntity?> GetOneByGuid(NonEmptyGuid traderGuid)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        return await GetOneByGuid(traderGuid, context);
    }

    public async Task<TraderEntity?> GetOneByGuid(NonEmptyGuid traderGuid, TraderDbContext context)
    {
        return await context.Traders.FirstOrDefaultAsync(trader => trader.Guid == traderGuid);
    }

    public async Task<TraderEntity> CreateAndSaveTrader(
        TraderConfiguration config,
        NonNegativeDecimalNumber initialPrice
    )
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        return await CreateAndSaveTrader(config, initialPrice, context);
    }

    public async Task<TraderEntity> CreateAndSaveTrader(
        TraderConfiguration config,
        NonNegativeDecimalNumber initialPrice,
        TraderDbContext context
    )
    {
        var now = DateTimeOffset.UtcNow;
        var initialTotalValue = config.InitialBaseInvestment * initialPrice + config.InitialQuoteInvestment;

        var traderEntity = new TraderEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserId = config.UserId,
            UserGuid = config.UserGuid,
            BrokerName = config.BrokerName,
            StrategyName = config.StrategyName,
            BaseCurrency = config.BaseCurrency,
            InitialPrice = initialPrice,
            InitialBaseBalance = config.InitialBaseInvestment,
            QuoteCurrency = config.QuoteCurrency,
            InitialQuoteBalance = config.InitialQuoteInvestment,
            InitialTotalValue = initialTotalValue,
            FirstStartedAt = now,
            LastStartedAt = now,
        };

        context.Traders.Add(traderEntity);

        return await Save(traderEntity, context);
    }

    public async Task<TraderEntity> Save(TraderEntity trader)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        return await Save(trader, context);
    }

    public async Task<TraderEntity> Save(TraderEntity trader, TraderDbContext context)
    {
        if (context.Entry(trader).State == EntityState.Detached)
        {
            context.Traders.Update(trader);
        }

        await context.SaveChangesAsync();

        return trader;
    }

    public async Task DeleteByGuid(NonEmptyGuid traderGuid)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        await DeleteByGuid(traderGuid, context);
    }

    public async Task DeleteByGuid(NonEmptyGuid traderGuid, TraderDbContext context)
    {
        var trader = context.Traders.SingleOrDefault(trader => trader.Guid == traderGuid);
        if (trader == null)
        {
            return;
        }

        await Delete(trader, context);
    }

    public async Task Delete(TraderEntity trader)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        await Delete(trader, context);
    }

    public async Task Delete(TraderEntity trader, TraderDbContext context)
    {
        context.Remove(trader);

        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<TraderEntity>> GetActiveTradersForUser(NonEmptyGuid userGuid)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();

        return await GetActiveTradersForUser(userGuid, context);
    }

    public async Task<IEnumerable<TraderEntity>> GetActiveTradersForUser(NonEmptyGuid userGuid, TraderDbContext context)
    {
        return await context.Traders
            .Where(
                trader => trader.UserGuid == userGuid
                && trader.Status == TraderStatusEnum.Running
            )
            .ToListAsync();
    }
}
