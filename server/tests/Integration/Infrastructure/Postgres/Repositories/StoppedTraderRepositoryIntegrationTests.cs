using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Infrastructure.Postgres.Repositories;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Postgres.Repositories;

public class StoppedTraderRepositoryIntegrationTests
{
    [Fact]
    public async Task SaveTraderAsStopped_ComputesFinalValueAndResultsFromBalances()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        await using var context = new StoppedTraderDbContext(options);
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);
        var userGuid = NonEmptyGuid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var trader = new TraderEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserId = 9,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(1m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(500m),
            InitialTotalValue = new NonNegativeDecimalNumber(600m),
            FirstStartedAt = startedAt,
            LastStartedAt = startedAt,
            Status = TraderStatusEnum.Running
        };

        var stoppedTrader = await repository.SaveTraderAsStopped(
            trader,
            finalPrice: new NonNegativeDecimalNumber(125m),
            finalBaseBalance: new NonNegativeDecimalNumber(2m),
            finalQuoteBalance: new NonNegativeDecimalNumber(220m),
            realizedResult: 30m);

        var persisted = await context.StoppedTraders.SingleAsync(cancellationToken);
        Assert.Equal(stoppedTrader.Guid, persisted.Guid);
        Assert.Equal(470m, persisted.FinalTotalValue.Value);
        Assert.Equal(-130m, persisted.UnrealizedResult);
        Assert.Equal(30m, persisted.RealizedResult);
        Assert.Equal(-100m, persisted.TotalResult);
        Assert.Equal(-130m / 600m, persisted.UnrealizedProfitMargin);
        Assert.Equal(30m / 600m, persisted.RealizedProfitMargin);
        Assert.Equal(-100m / 600m, persisted.TotalProfitMargin);
        Assert.Equal(startedAt, persisted.StartedAt);
    }

    [Fact]
    public async Task GetAllForUser_ReturnsOnlyMatchingStoppedTraders()
    {
        var matchingUserGuid = NonEmptyGuid.NewGuid();
        var otherUserGuid = NonEmptyGuid.NewGuid();
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        await using var context = new StoppedTraderDbContext(options);
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);

        context.StoppedTraders.AddRange(
            CreateStoppedTrader(1, matchingUserGuid, NonEmptyGuid.NewGuid()),
            CreateStoppedTrader(1, matchingUserGuid, NonEmptyGuid.NewGuid()),
            CreateStoppedTrader(2, otherUserGuid, NonEmptyGuid.NewGuid())
        );
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var traders = (await repository.GetAllForUser(matchingUserGuid)).ToList();

        Assert.Equal(2, traders.Count);
        Assert.All(traders, trader => Assert.Equal(matchingUserGuid, trader.UserGuid));
    }

    [Fact]
    public async Task GetAllForUser_WithUserEntity_ReturnsOnlyMatchingStoppedTraders()
    {
        await using var context = CreateContext(out var contextFactory);
        var user = new UserEntity
        {
            Id = 21,
            Guid = NonEmptyGuid.NewGuid(),
            UserName = "stopped@example.com",
            NormalizedUserName = "STOPPED@EXAMPLE.COM",
            Email = "stopped@example.com",
            NormalizedEmail = "STOPPED@EXAMPLE.COM"
        };
        context.StoppedTraders.AddRange(
            CreateStoppedTrader(user.Id, user.Guid, NonEmptyGuid.NewGuid()),
            CreateStoppedTrader(22, NonEmptyGuid.NewGuid(), NonEmptyGuid.NewGuid()));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new StoppedTraderRepository(contextFactory);

        var traders = (await repository.GetAllForUser(user)).ToList();

        Assert.Single(traders);
        Assert.Equal(user.Id, traders[0].UserId);
    }

    [Fact]
    public async Task GetAllForUser_ByUserId_ReturnsOnlyMatchingStoppedTraders()
    {
        await using var context = CreateContext(out var contextFactory);
        var matchingUserGuid = NonEmptyGuid.NewGuid();
        context.StoppedTraders.AddRange(
            CreateStoppedTrader(31, matchingUserGuid, NonEmptyGuid.NewGuid()),
            CreateStoppedTrader(31, matchingUserGuid, NonEmptyGuid.NewGuid()),
            CreateStoppedTrader(32, NonEmptyGuid.NewGuid(), NonEmptyGuid.NewGuid()));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new StoppedTraderRepository(contextFactory);

        var traders = (await repository.GetAllForUser(31)).ToList();

        Assert.Equal(2, traders.Count);
        Assert.All(traders, trader => Assert.Equal((ulong)31, trader.UserId));
    }

    [Fact]
    public async Task GetAllForUser_WithDbContext_FiltersByUserIdAndUserGuid()
    {
        await using var context = CreateContext(out var contextFactory);
        var matchingUserGuid = NonEmptyGuid.NewGuid();
        context.StoppedTraders.AddRange(
            CreateStoppedTrader(41, matchingUserGuid, NonEmptyGuid.NewGuid()),
            CreateStoppedTrader(41, NonEmptyGuid.NewGuid(), NonEmptyGuid.NewGuid()),
            CreateStoppedTrader(42, matchingUserGuid, NonEmptyGuid.NewGuid()));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new StoppedTraderRepository(contextFactory);

        var byId = (await repository.GetAllForUser(41, context)).ToList();
        var byGuid = (await repository.GetAllForUser(matchingUserGuid, context)).ToList();

        Assert.Equal(2, byId.Count);
        Assert.All(byId, trader => Assert.Equal((ulong)41, trader.UserId));
        Assert.Equal(2, byGuid.Count);
        Assert.All(byGuid, trader => Assert.Equal(matchingUserGuid, trader.UserGuid));
    }

    [Fact]
    public async Task GetOneByUserIdAndGuid_ReturnsMatchingStoppedTrader()
    {
        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        await using var context = new StoppedTraderDbContext(options);
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);

        context.StoppedTraders.Add(CreateStoppedTrader(7, userGuid, traderGuid));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var trader = await repository.GetOneByUserIdAndGuid(7, traderGuid);

        Assert.NotNull(trader);
        Assert.Equal(traderGuid, trader!.Guid);
    }

    [Fact]
    public async Task GetOneByGuid_ReturnsMatchingStoppedTrader()
    {
        var traderGuid = NonEmptyGuid.NewGuid();
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        await using var context = new StoppedTraderDbContext(options);
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);

        context.StoppedTraders.Add(CreateStoppedTrader(13, NonEmptyGuid.NewGuid(), traderGuid));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var trader = await repository.GetOneByGuid(traderGuid);

        Assert.NotNull(trader);
        Assert.Equal(traderGuid, trader!.Guid);
    }

    [Fact]
    public async Task GetOneByGuid_ReturnsNull_WhenStoppedTraderMissing()
    {
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);

        var found = await repository.GetOneByGuid(NonEmptyGuid.NewGuid());

        Assert.Null(found);
    }

    [Fact]
    public async Task Delete_RemovesStoppedTraderEntity()
    {
        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        await using var context = new StoppedTraderDbContext(options);
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);

        var stoppedTrader = CreateStoppedTrader(15, userGuid, traderGuid);
        context.StoppedTraders.Add(stoppedTrader);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await repository.Delete(stoppedTrader);

        Assert.Null(await repository.GetOneByGuid(traderGuid));
    }

    [Fact]
    public async Task DeleteByGuid_RemovesMatchingStoppedTrader()
    {
        var userGuid = NonEmptyGuid.NewGuid();
        var matchingGuid = NonEmptyGuid.NewGuid();
        var otherGuid = NonEmptyGuid.NewGuid();
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        await using var context = new StoppedTraderDbContext(options);
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);

        context.StoppedTraders.AddRange(
            CreateStoppedTrader(11, userGuid, matchingGuid),
            CreateStoppedTrader(11, userGuid, otherGuid)
        );
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await repository.DeleteByGuid(matchingGuid);

        Assert.Null(await repository.GetOneByGuid(matchingGuid));
        Assert.NotNull(await repository.GetOneByGuid(otherGuid));
    }

    [Fact]
    public async Task Save_PersistsUpdatedStoppedTraderFromAnotherContext()
    {
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        var contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        var repository = new StoppedTraderRepository(contextFactory);
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var trader = new TraderEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserId = 9,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(1m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(500m),
            InitialTotalValue = new NonNegativeDecimalNumber(600m),
            FirstStartedAt = startedAt,
            LastStartedAt = startedAt,
            Status = TraderStatusEnum.Running
        };
        var created = await repository.SaveTraderAsStopped(
            trader,
            finalPrice: new NonNegativeDecimalNumber(125m),
            finalBaseBalance: new NonNegativeDecimalNumber(2m),
            finalQuoteBalance: new NonNegativeDecimalNumber(220m),
            realizedResult: 30m);

        var detached = await repository.GetOneByGuid(created.Guid);
        Assert.NotNull(detached);
        detached.FinalPrice = new NonNegativeDecimalNumber(130m);

        var saved = await repository.Save(detached);

        Assert.Same(detached, saved);
        var persisted = await repository.GetOneByGuid(created.Guid);
        Assert.NotNull(persisted);
        Assert.True(
            persisted.FinalPrice.Value == 130m,
            $"Saving stopped trader '{created.Guid}' from a separate DbContext did not persist the updated FinalPrice. Expected 130, but reloading from the database returned {persisted.FinalPrice.Value}.");
    }

    private static StoppedTraderEntity CreateStoppedTrader(
        ulong userId,
        NonEmptyGuid userGuid,
        NonEmptyGuid traderGuid,
        DateTimeOffset? startedAt = null)
    {
        var whenStarted = startedAt ?? DateTimeOffset.UtcNow.AddHours(-2);
        return new StoppedTraderEntity
        {
            Guid = traderGuid,
            UserId = userId,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(1m),
            FinalPrice = new NonNegativeDecimalNumber(125m),
            FinalBaseBalance = new NonNegativeDecimalNumber(2m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(500m),
            FinalQuoteBalance = new NonNegativeDecimalNumber(220m),
            InitialTotalValue = new NonNegativeDecimalNumber(600m),
            FinalTotalValue = new NonNegativeDecimalNumber(470m),
            UnrealizedResult = -130m,
            RealizedResult = 30m,
            TotalResult = -100m,
            UnrealizedProfitMargin = -130m / 600m,
            RealizedProfitMargin = 30m / 600m,
            TotalProfitMargin = -100m / 600m,
            StartedAt = whenStarted,
            StoppedAt = whenStarted.AddMinutes(30)
        };
    }

    private static StoppedTraderDbContext CreateContext(
        out IDbContextFactory<StoppedTraderDbContext> contextFactory)
    {
        var options = new DbContextOptionsBuilder<StoppedTraderDbContext>()
            .UseInMemoryDatabase($"stopped-trader-repo-{Guid.NewGuid()}")
            .Options;
        contextFactory = new TestDbContextFactory<StoppedTraderDbContext>(
            options,
            contextOptions => new StoppedTraderDbContext(contextOptions));
        return new StoppedTraderDbContext(options);
    }
}
