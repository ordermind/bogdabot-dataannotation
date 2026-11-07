using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Infrastructure.Postgres.Repositories;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Postgres.Repositories;

public class UserRepositoryIntegrationTests
{
    [Fact]
    public async Task GetAllUsersWithActiveTraders_ReturnsAllDistinctActiveUsers()
    {
        var databaseName = $"user-repo-tests-{Guid.NewGuid()}";

        await using var userContext = CreateUserContext(databaseName);
        await using var traderContext = CreateTraderContext(databaseName);

        var firstActiveUser = CreateUser(1, NonEmptyGuid.NewGuid(), "first@example.com");
        var secondActiveUser = CreateUser(2, NonEmptyGuid.NewGuid(), "second@example.com");

        userContext.Users.AddRange(firstActiveUser, secondActiveUser);
        await userContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        traderContext.Traders.AddRange(
            CreateTrader(firstActiveUser.Id, firstActiveUser.Guid, TraderStatusEnum.Running),
            CreateTrader(secondActiveUser.Id, secondActiveUser.Guid, TraderStatusEnum.Running)
        );
        await traderContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = CreateRepository(databaseName);

        var users = (await repository.GetAllUsersWithActiveTraders()).ToList();

        Assert.Equal(2, users.Count);
        Assert.Contains(users, user => user.Id == firstActiveUser.Id);
        Assert.Contains(users, user => user.Id == secondActiveUser.Id);
    }

    [Fact]
    public async Task GetAllUsersWithActiveTraders_ReturnsDistinctUsersWithRunningTraders()
    {
        var databaseName = $"user-repo-tests-{Guid.NewGuid()}";

        await using var userContext = CreateUserContext(databaseName);
        await using var traderContext = CreateTraderContext(databaseName);

        var activeUser = CreateUser(1, NonEmptyGuid.NewGuid(), "active@example.com");
        var inactiveUser = CreateUser(2, NonEmptyGuid.NewGuid(), "inactive@example.com");

        userContext.Users.AddRange(activeUser, inactiveUser);
        await userContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        traderContext.Traders.AddRange(
            CreateTrader(activeUser.Id, activeUser.Guid, TraderStatusEnum.Running),
            CreateTrader(activeUser.Id, activeUser.Guid, TraderStatusEnum.Running),
            CreateTrader(inactiveUser.Id, inactiveUser.Guid, TraderStatusEnum.Failed)
        );
        await traderContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = CreateRepository(databaseName);

        var users = (await repository.GetAllUsersWithActiveTraders()).ToList();

        Assert.Single(users);
        Assert.Equal(activeUser.Id, users[0].Id);
    }

    [Fact]
    public async Task GetAllUsersWithActiveTraders_ReturnsEmpty_WhenNoRunningTradersExist()
    {
        var databaseName = $"user-repo-tests-{Guid.NewGuid()}";

        await using var userContext = CreateUserContext(databaseName);
        await using var traderContext = CreateTraderContext(databaseName);

        var user = CreateUser(1, NonEmptyGuid.NewGuid(), "user@example.com");

        userContext.Users.Add(user);
        await userContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        traderContext.Traders.Add(CreateTrader(user.Id, user.Guid, TraderStatusEnum.Failed));
        await traderContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = CreateRepository(databaseName);

        var users = await repository.GetAllUsersWithActiveTraders();

        Assert.Empty(users);
    }

    [Fact]
    public async Task GetAllUsersWithActiveTraders_IgnoresRunningTradersWithMissingUserRecords()
    {
        var databaseName = $"user-repo-tests-{Guid.NewGuid()}";

        await using var userContext = CreateUserContext(databaseName);
        await using var traderContext = CreateTraderContext(databaseName);

        var existingUser = CreateUser(1, NonEmptyGuid.NewGuid(), "existing@example.com");
        userContext.Users.Add(existingUser);
        await userContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        traderContext.Traders.AddRange(
            CreateTrader(existingUser.Id, existingUser.Guid, TraderStatusEnum.Running),
            CreateTrader(999, NonEmptyGuid.NewGuid(), TraderStatusEnum.Running)
        );
        await traderContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = CreateRepository(databaseName);

        var users = (await repository.GetAllUsersWithActiveTraders()).ToList();

        Assert.Single(users);
        Assert.Equal(existingUser.Id, users[0].Id);
    }

    private static UserDbContext CreateUserContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new UserDbContext(options);
    }

    private static TraderDbContext CreateTraderContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TraderDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new TraderDbContext(options);
    }

    private static UserRepository CreateRepository(string databaseName)
    {
        var userOptions = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var traderOptions = new DbContextOptionsBuilder<TraderDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var userContextFactory = new TestDbContextFactory<UserDbContext>(userOptions, options => new UserDbContext(options));
        var traderContextFactory = new TestDbContextFactory<TraderDbContext>(traderOptions, options => new TraderDbContext(options));

        return new UserRepository(userContextFactory, traderContextFactory);
    }

    private static UserEntity CreateUser(ulong id, NonEmptyGuid guid, string email)
    {
        return new UserEntity
        {
            Id = id,
            Guid = guid,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "hash"
        };
    }

    private static TraderEntity CreateTrader(ulong userId, NonEmptyGuid userGuid, TraderStatusEnum status)
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
            InitialPrice = new NonNegativeDecimalNumber(10m),
            InitialBaseBalance = new NonNegativeDecimalNumber(10m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(5m),
            InitialTotalValue = new NonNegativeDecimalNumber(15m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = status
        };
    }
}
