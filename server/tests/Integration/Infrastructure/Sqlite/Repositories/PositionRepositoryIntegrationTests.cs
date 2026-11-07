using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;
using Bogdabot.Server.Infrastructure.Sqlite.Config;
using Bogdabot.Server.Infrastructure.Sqlite.DbContexts;
using Bogdabot.Server.Infrastructure.Sqlite.Factories;
using Bogdabot.Server.Infrastructure.Sqlite.Repositories;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Factories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Sqlite.Repositories;

public class PositionRepositoryIntegrationTests
{
    [Fact]
    public async Task GetSummary_CalculatesBalancesFromOpenAndClosedPositions()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"positions-{Guid.NewGuid()}.db");
        var userGuid = NonEmptyGuid.NewGuid();
        const string actorId = "trader-test";
        var contextFactory = CreateContextFactory(databasePath);

        try
        {
            await using (var context = contextFactory.Create<PositionDbContext>(userGuid, actorId))
            {
                await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
                context.Positions.AddRange(
                    CreatePosition(2m, 100m, 1m, null, null),
                    CreatePosition(1m, 50m, 0.5m, null, null),
                    CreatePosition(3m, 40m, 1m, DateTimeOffset.UtcNow, 30m));
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var repository = new PositionRepository(contextFactory, userGuid, actorId);
            await using var summaryContext = contextFactory.Create<PositionDbContext>(userGuid, actorId);
            var summary = await repository.GetSummary(new NonNegativeDecimalNumber(1_000m), summaryContext);

            Assert.Equal(3m, summary.CurrentBaseBalance.Value);
            Assert.Equal(778.5m, summary.CurrentQuoteBalance.Value);
            Assert.Equal(30m, summary.RealizedResult.Value);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task GetSummary_ReturnsInitialQuoteBalance_WhenThereAreNoPositions()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"positions-{Guid.NewGuid()}.db");
        var userGuid = NonEmptyGuid.NewGuid();
        const string actorId = "trader-empty";
        var contextFactory = CreateContextFactory(databasePath);

        try
        {
            await using (var context = contextFactory.Create<PositionDbContext>(userGuid, actorId))
            {
                await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            }

            var repository = new PositionRepository(contextFactory, userGuid, actorId);
            var summary = await repository.GetSummary(new NonNegativeDecimalNumber(250m));

            Assert.Equal(0m, summary.CurrentBaseBalance.Value);
            Assert.Equal(250m, summary.CurrentQuoteBalance.Value);
            Assert.Equal(0m, summary.RealizedResult.Value);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static TraderDbContextFactory CreateContextFactory(string databasePath)
    {
        var config = new SqliteDatabaseConfig(databasePath);
        var configFactory = Substitute.For<ITraderDatabaseConfigFactory>();
        configFactory.Create(Arg.Any<NonEmptyGuid>(), Arg.Any<string>()).Returns(config);

        return new TraderDbContextFactory(configFactory, new DatabaseContextFactory());
    }

    private static PositionEntity CreatePosition(
        decimal quantity,
        decimal buyPrice,
        decimal buyFee,
        DateTimeOffset? soldAt,
        decimal? realizedResult)
    {
        return new PositionEntity
        {
            Base = "BTC",
            Quote = "EUR",
            Quantity = new PositiveDecimalNumber(quantity),
            DesiredProfitMargin = new PositiveDecimalNumber(0.1m),
            BuyOrderExternalId = Guid.NewGuid().ToString(),
            BoughtAt = DateTimeOffset.UtcNow,
            BuyPrice = new PositiveDecimalNumber(buyPrice),
            BuyFee = new PositiveDecimalNumber(buyFee),
            SoldAt = soldAt,
            SellPrice = soldAt.HasValue ? new PositiveDecimalNumber(45m) : null,
            SellFee = soldAt.HasValue ? new PositiveDecimalNumber(1m) : null,
            RealizedResult = realizedResult
        };
    }
}
