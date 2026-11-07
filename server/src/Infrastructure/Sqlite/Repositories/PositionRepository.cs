using Bogdabot.Server.Infrastructure.Sqlite.DbContexts;
using Bogdabot.Server.Infrastructure.Sqlite.Factories;
using Bogdabot.Server.Shared.Trading.DTO;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Sqlite.Repositories;

/// <summary>
/// The overload for each method is meant to give you the option of creating the db context outside of the repository. If you just need to use a single method, call the one without a context. If you need to combine multiple queries for the same repository, it might be a good idea to create the context outside and then call the overloaded method that allows passing the context as a parameter.
/// </summary>
public class PositionRepository(
    TraderDbContextFactory contextFactory,
    NonEmptyGuid userGuid,
    string actorId
)
    : IPositionRepository
{
    private TraderDbContextFactory ContextFactory { get; } = contextFactory;
    private NonEmptyGuid UserGuid { get; } = userGuid;
    private string ActorId { get; } = actorId;

    public async Task<NonNegativeDecimalNumber> GetTotalBaseBalance()
    {
        await using var context = ContextFactory.Create<PositionDbContext>(UserGuid, ActorId);

        return await GetTotalBaseBalance(context);
    }

    public async Task<NonNegativeDecimalNumber> GetTotalBaseBalance(PositionDbContext context)
    {
        var result = await context.Positions
            .Where(position => position.SoldAt == null)
            .SumAsync(position => position.Quantity);

        return new NonNegativeDecimalNumber(result);
    }

    public async Task<NonNegativeDecimalNumber> GetTotalQuoteBalance(
        NonNegativeDecimalNumber initialQuoteBalance)
    {
        await using var context = ContextFactory.Create<PositionDbContext>(UserGuid, ActorId);

        return await GetTotalQuoteBalance(initialQuoteBalance, context);
    }

    public async Task<NonNegativeDecimalNumber> GetTotalQuoteBalance(
        NonNegativeDecimalNumber initialQuoteBalance, PositionDbContext context)
    {
        var totalRealizedResult = await GetTotalRealizedResult(context);

        var totalOpenPositionCost = await context.Positions
            .Where(position => position.SoldAt == null)
            .SumAsync(position => position.Quantity * position.BuyPrice + position.BuyFee);

        var result = (decimal)initialQuoteBalance
            + totalRealizedResult
            - totalOpenPositionCost;

        return new NonNegativeDecimalNumber(result);
    }

    public async Task<NonNegativeDecimalNumber> GetTotalRealizedResult()
    {
        await using var context = ContextFactory.Create<PositionDbContext>(UserGuid, ActorId);

        return await GetTotalRealizedResult(context);
    }

    public async Task<NonNegativeDecimalNumber> GetTotalRealizedResult(PositionDbContext context)
    {
        var result = await context.Positions
            .SumAsync(position => position.RealizedResult) ?? 0m;

        return NonNegativeDecimalNumber.FromValue(result);
    }

    public async Task<PositionSummary> GetSummary(
        NonNegativeDecimalNumber initialQuoteBalance)
    {
        await using var context = ContextFactory.Create<PositionDbContext>(UserGuid, ActorId);

        return await GetSummary(initialQuoteBalance, context);
    }

    public async Task<PositionSummary> GetSummary(
        NonNegativeDecimalNumber initialQuoteBalance,
        PositionDbContext context)
    {
        var result = await context.Positions
            .GroupBy(_ => 1)
            .Select(group => new
            {
                BaseBalance = group
                    .Where(position => position.SoldAt == null)
                    .Sum(position => position.Quantity),

                RealizedResult = group
                    .Sum(position => position.RealizedResult) ?? 0m,

                OpenPositionCost = group
                    .Where(position => position.SoldAt == null)
                    .Sum(position =>
                        position.Quantity * position.BuyPrice + position.BuyFee)
            })
            .SingleOrDefaultAsync();

        var baseBalance = result?.BaseBalance ?? 0m;
        var realizedResult = result?.RealizedResult ?? 0m;
        var openPositionCost = result?.OpenPositionCost ?? 0m;

        var quoteBalance =
            (decimal)initialQuoteBalance
            + realizedResult
            - openPositionCost;

        return new PositionSummary
        {
            CurrentBaseBalance = NonNegativeDecimalNumber.FromValue(baseBalance),
            CurrentQuoteBalance = NonNegativeDecimalNumber.FromValue(quoteBalance),
            RealizedResult = NonNegativeDecimalNumber.FromValue(realizedResult)
        };
    }
}
