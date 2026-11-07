using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Infrastructure.Postgres.Repositories;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Postgres.Repositories;

public class TraderRepositoryIntegrationTests
{
    [Fact]
    public async Task CreateAndSaveTrader_PersistsTrader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var repository = new TraderRepository(contextFactory);

        var command = CreateConfig();
        var initialPrice = new NonNegativeDecimalNumber(123.45m);

        var entity = await repository.CreateAndSaveTrader(command, initialPrice);

        var persisted = await context.Traders.SingleAsync(x => x.Guid == entity.Guid, cancellationToken);
        Assert.Equal(entity.Guid, persisted.Guid);
        Assert.Equal(command.UserId, persisted.UserId);
        Assert.Equal(command.UserGuid, persisted.UserGuid);
        Assert.Equal(initialPrice, persisted.InitialPrice);
        Assert.Equal(command.InitialBaseInvestment, persisted.InitialBaseBalance);
        Assert.Equal(12_395m, persisted.InitialTotalValue.Value);
    }

    [Fact]
    public async Task GetOneByGuid_ReturnsMatchingTrader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var traderGuid = NonEmptyGuid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Traders.Add(new TraderEntity
        {
            Guid = traderGuid,
            UserId = 2,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("ETH"),
            InitialPrice = new NonNegativeDecimalNumber(50m),
            InitialBaseBalance = new NonNegativeDecimalNumber(40m),
            QuoteCurrency = new CurrencySymbol("USD"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(10m),
            InitialTotalValue = new NonNegativeDecimalNumber(50m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = TraderStatusEnum.Created
        });
        await context.SaveChangesAsync(cancellationToken);

        var repository = new TraderRepository(contextFactory);

        var found = await repository.GetOneByGuid(traderGuid);

        Assert.NotNull(found);
        Assert.Equal(traderGuid, found!.Guid);
    }

    [Fact]
    public async Task GetAllForUserByUserId_ReturnsOnlyMatchingTraders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var firstMatchingGuid = NonEmptyGuid.NewGuid();
        var otherUserGuid = NonEmptyGuid.NewGuid();
        var secondMatchingGuid = NonEmptyGuid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Traders.AddRange(
            new TraderEntity
            {
                Guid = firstMatchingGuid,
                UserId = 1,
                UserGuid = NonEmptyGuid.NewGuid(),
                BrokerName = BrokerEnum.IBKR,
                StrategyName = TradingStrategyEnum.SpotGrid,
                BaseCurrency = new CurrencySymbol("BTC"),
                InitialPrice = new NonNegativeDecimalNumber(50m),
                InitialBaseBalance = new NonNegativeDecimalNumber(40m),
                QuoteCurrency = new CurrencySymbol("EUR"),
                InitialQuoteBalance = new NonNegativeDecimalNumber(10m),
                InitialTotalValue = new NonNegativeDecimalNumber(50m),
                FirstStartedAt = now,
                LastStartedAt = now,
                Status = TraderStatusEnum.Created
            },
            new TraderEntity
            {
                Guid = otherUserGuid,
                UserId = 2,
                UserGuid = NonEmptyGuid.NewGuid(),
                BrokerName = BrokerEnum.IBKR,
                StrategyName = TradingStrategyEnum.SpotGrid,
                BaseCurrency = new CurrencySymbol("ETH"),
                InitialPrice = new NonNegativeDecimalNumber(60m),
                InitialBaseBalance = new NonNegativeDecimalNumber(50m),
                QuoteCurrency = new CurrencySymbol("USD"),
                InitialQuoteBalance = new NonNegativeDecimalNumber(10m),
                InitialTotalValue = new NonNegativeDecimalNumber(60m),
                FirstStartedAt = now,
                LastStartedAt = now,
                Status = TraderStatusEnum.Created
            },
            new TraderEntity
            {
                Guid = secondMatchingGuid,
                UserId = 1,
                UserGuid = NonEmptyGuid.NewGuid(),
                BrokerName = BrokerEnum.IBKR,
                StrategyName = TradingStrategyEnum.SpotGrid,
                BaseCurrency = new CurrencySymbol("SOL"),
                InitialPrice = new NonNegativeDecimalNumber(70m),
                InitialBaseBalance = new NonNegativeDecimalNumber(60m),
                QuoteCurrency = new CurrencySymbol("USD"),
                InitialQuoteBalance = new NonNegativeDecimalNumber(10m),
                InitialTotalValue = new NonNegativeDecimalNumber(70m),
                FirstStartedAt = now,
                LastStartedAt = now,
                Status = TraderStatusEnum.Created
            });
        await context.SaveChangesAsync(cancellationToken);

        var repository = new TraderRepository(contextFactory);

        var traders = (await repository.GetAllForUser(1)).ToList();

        Assert.Equal(2, traders.Count);
        Assert.All(traders, trader => Assert.Equal((ulong)1, trader.UserId));
        Assert.Contains(traders, trader => trader.Guid == firstMatchingGuid);
        Assert.Contains(traders, trader => trader.Guid == secondMatchingGuid);
    }

    [Fact]
    public async Task GetAllForUser_WithUserEntity_ReturnsOnlyMatchingTraders()
    {
        await using var context = CreateContext(out var contextFactory);
        var user = new Bogdabot.Server.Account.Entities.UserEntity
        {
            Id = 31,
            Guid = NonEmptyGuid.NewGuid(),
            UserName = "trader@example.com",
            NormalizedUserName = "TRADER@EXAMPLE.COM",
            Email = "trader@example.com",
            NormalizedEmail = "TRADER@EXAMPLE.COM"
        };
        context.Traders.AddRange(
            CreateTrader(user.Id, NonEmptyGuid.NewGuid()),
            CreateTrader(32, NonEmptyGuid.NewGuid()));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new TraderRepository(contextFactory);

        var traders = (await repository.GetAllForUser(user)).ToList();

        Assert.Single(traders);
        Assert.Equal(user.Id, traders[0].UserId);
    }

    [Fact]
    public async Task GetAllForUser_ByUserGuid_ReturnsOnlyMatchingTraders()
    {
        await using var context = CreateContext(out var contextFactory);
        var matchingUserGuid = NonEmptyGuid.NewGuid();
        context.Traders.AddRange(
            CreateTrader(41, matchingUserGuid),
            CreateTrader(42, matchingUserGuid),
            CreateTrader(41, NonEmptyGuid.NewGuid()));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new TraderRepository(contextFactory);

        var traders = (await repository.GetAllForUser(matchingUserGuid)).ToList();

        Assert.Equal(2, traders.Count);
        Assert.All(traders, trader => Assert.Equal(matchingUserGuid, trader.UserGuid));
    }

    [Fact]
    public async Task GetAllForUser_WithDbContext_FiltersByUserIdAndUserGuid()
    {
        await using var context = CreateContext(out var contextFactory);
        var matchingUserGuid = NonEmptyGuid.NewGuid();
        context.Traders.AddRange(
            CreateTrader(51, matchingUserGuid),
            CreateTrader(51, NonEmptyGuid.NewGuid()),
            CreateTrader(52, matchingUserGuid));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new TraderRepository(contextFactory);

        var byId = (await repository.GetAllForUser(51, context)).ToList();
        var byGuid = (await repository.GetAllForUser(matchingUserGuid, context)).ToList();

        Assert.Equal(2, byId.Count);
        Assert.All(byId, trader => Assert.Equal((ulong)51, trader.UserId));
        Assert.Equal(2, byGuid.Count);
        Assert.All(byGuid, trader => Assert.Equal(matchingUserGuid, trader.UserGuid));
    }

    [Fact]
    public async Task GetOneByUserIdAndGuid_ReturnsMatchingTrader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var traderGuid = NonEmptyGuid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Traders.Add(new TraderEntity
        {
            Guid = traderGuid,
            UserId = 12,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("ETH"),
            InitialPrice = new NonNegativeDecimalNumber(50m),
            InitialBaseBalance = new NonNegativeDecimalNumber(40m),
            QuoteCurrency = new CurrencySymbol("USD"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(10m),
            InitialTotalValue = new NonNegativeDecimalNumber(50m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = TraderStatusEnum.Created
        });
        await context.SaveChangesAsync(cancellationToken);

        var repository = new TraderRepository(contextFactory);

        var trader = await repository.GetOneByUserIdAndGuid(12, traderGuid);

        Assert.NotNull(trader);
        Assert.Equal(traderGuid, trader!.Guid);
    }

    [Fact]
    public async Task GetOneByGuid_ReturnsNull_WhenTraderMissing()
    {
        await using var context = CreateContext(out var contextFactory);
        var repository = new TraderRepository(contextFactory);

        var found = await repository.GetOneByGuid(NonEmptyGuid.NewGuid());

        Assert.Null(found);
    }

    [Fact]
    public async Task Save_PersistsDetachedTraderUpdateAcrossContexts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var traderGuid = NonEmptyGuid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Traders.Add(new TraderEntity
        {
            Guid = traderGuid,
            UserId = 4,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(150m),
            InitialBaseBalance = new NonNegativeDecimalNumber(100m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(50m),
            InitialTotalValue = new NonNegativeDecimalNumber(150m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = TraderStatusEnum.Created
        });
        await context.SaveChangesAsync(cancellationToken);

        var repository = new TraderRepository(contextFactory);
        var trader = await repository.GetOneByGuid(traderGuid);
        Assert.NotNull(trader);
        trader.Status = TraderStatusEnum.Failed;

        var saved = await repository.Save(trader);

        Assert.Same(trader, saved);

        var persisted = await repository.GetOneByGuid(traderGuid);
        Assert.NotNull(persisted);
        Assert.True(
            persisted.Status == TraderStatusEnum.Failed,
            $"Saving trader '{traderGuid}' from a separate DbContext did not persist the updated Status. Expected '{TraderStatusEnum.Failed}', but reloading from the database returned '{persisted.Status}'.");
    }

    [Fact]
    public async Task GetActiveTradersForUser_ReturnsOnlyRunningTraders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var userGuid = NonEmptyGuid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Traders.AddRange(
            new TraderEntity
            {
                Guid = NonEmptyGuid.NewGuid(),
                UserId = 7,
                UserGuid = userGuid,
                BrokerName = BrokerEnum.IBKR,
                StrategyName = TradingStrategyEnum.SpotGrid,
                BaseCurrency = new CurrencySymbol("BTC"),
                InitialPrice = new NonNegativeDecimalNumber(100m),
                InitialBaseBalance = new NonNegativeDecimalNumber(10m),
                QuoteCurrency = new CurrencySymbol("EUR"),
                InitialQuoteBalance = new NonNegativeDecimalNumber(50m),
                InitialTotalValue = new NonNegativeDecimalNumber(150m),
                FirstStartedAt = now,
                LastStartedAt = now,
                Status = TraderStatusEnum.Running
            },
            new TraderEntity
            {
                Guid = NonEmptyGuid.NewGuid(),
                UserId = 7,
                UserGuid = userGuid,
                BrokerName = BrokerEnum.IBKR,
                StrategyName = TradingStrategyEnum.SpotGrid,
                BaseCurrency = new CurrencySymbol("ETH"),
                InitialPrice = new NonNegativeDecimalNumber(90m),
                InitialBaseBalance = new NonNegativeDecimalNumber(8m),
                QuoteCurrency = new CurrencySymbol("USD"),
                InitialQuoteBalance = new NonNegativeDecimalNumber(40m),
                InitialTotalValue = new NonNegativeDecimalNumber(130m),
                FirstStartedAt = now,
                LastStartedAt = now,
                Status = TraderStatusEnum.Created
            },
            new TraderEntity
            {
                Guid = NonEmptyGuid.NewGuid(),
                UserId = 8,
                UserGuid = NonEmptyGuid.NewGuid(),
                BrokerName = BrokerEnum.IBKR,
                StrategyName = TradingStrategyEnum.SpotGrid,
                BaseCurrency = new CurrencySymbol("SOL"),
                InitialPrice = new NonNegativeDecimalNumber(80m),
                InitialBaseBalance = new NonNegativeDecimalNumber(9m),
                QuoteCurrency = new CurrencySymbol("USD"),
                InitialQuoteBalance = new NonNegativeDecimalNumber(30m),
                InitialTotalValue = new NonNegativeDecimalNumber(110m),
                FirstStartedAt = now,
                LastStartedAt = now,
                Status = TraderStatusEnum.Running
            });
        await context.SaveChangesAsync(cancellationToken);

        var repository = new TraderRepository(contextFactory);

        var traders = (await repository.GetActiveTradersForUser(userGuid)).ToList();

        Assert.Single(traders);
        Assert.All(traders, trader => Assert.Equal(TraderStatusEnum.Running, trader.Status));
        Assert.All(traders, trader => Assert.Equal(userGuid, trader.UserGuid));
    }

    [Fact]
    public async Task DeleteByGuid_RemovesMatchingTrader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var traderGuid = NonEmptyGuid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Traders.Add(new TraderEntity
        {
            Guid = traderGuid,
            UserId = 3,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(150m),
            InitialBaseBalance = new NonNegativeDecimalNumber(100m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(50m),
            InitialTotalValue = new NonNegativeDecimalNumber(150m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = TraderStatusEnum.Created
        });
        await context.SaveChangesAsync(cancellationToken);

        var repository = new TraderRepository(contextFactory);

        await repository.DeleteByGuid(traderGuid);

        Assert.Null(await context.Traders.SingleOrDefaultAsync(x => x.Guid == traderGuid, cancellationToken));
    }

    [Fact]
    public async Task DeleteByGuid_DoesNothing_WhenTraderMissing()
    {
        await using var context = CreateContext(out var contextFactory);
        var repository = new TraderRepository(contextFactory);

        await repository.DeleteByGuid(NonEmptyGuid.NewGuid());

        Assert.Empty(context.Traders);
    }

    [Fact]
    public async Task Delete_RemovesTrader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateContext(out var contextFactory);
        var now = DateTimeOffset.UtcNow;
        var trader = new TraderEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserId = 3,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(150m),
            InitialBaseBalance = new NonNegativeDecimalNumber(100m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(50m),
            InitialTotalValue = new NonNegativeDecimalNumber(150m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = TraderStatusEnum.Created
        };
        context.Traders.Add(trader);
        await context.SaveChangesAsync(cancellationToken);

        var repository = new TraderRepository(contextFactory);

        await repository.Delete(trader);

        Assert.Null(await context.Traders.SingleOrDefaultAsync(x => x.Guid == trader.Guid, cancellationToken));
    }

    private static TraderDbContext CreateContext(out IDbContextFactory<TraderDbContext> contextFactory)
    {
        var options = new DbContextOptionsBuilder<TraderDbContext>()
            .UseInMemoryDatabase($"trader-repo-{Guid.NewGuid()}")
            .Options;

        contextFactory = new TestDbContextFactory<TraderDbContext>(options, contextOptions => new TraderDbContext(contextOptions));
        return new TraderDbContext(options);
    }

    private static TraderConfiguration CreateConfig()
    {
        return new TraderConfiguration
        {
            UserId = 9,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialBaseInvestment = new NonNegativeDecimalNumber(100m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(50m)
        };
    }

    private static TraderEntity CreateTrader(ulong userId, NonEmptyGuid userGuid)
    {
        var now = DateTimeOffset.UtcNow;
        return new TraderEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserId = userId,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(1m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(50m),
            InitialTotalValue = new NonNegativeDecimalNumber(150m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = TraderStatusEnum.Created
        };
    }
}
