using Bogdabot.Server.Infrastructure.Sqlite.DbContexts;
using Bogdabot.Server.Shared.Trading.DTO;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Trading.Repositories;

/// <summary>
/// The overload for each method is meant to give you the option of creating the db context outside of the repository. If you just need to use a single method, call the one without a context. If you need to combine multiple queries for the same repository, it might be a good idea to create the context outside and then call the overloaded method that allows passing the context as a parameter.
/// </summary>
public interface IPositionRepository
{
    Task<NonNegativeDecimalNumber> GetTotalBaseBalance();
    Task<NonNegativeDecimalNumber> GetTotalBaseBalance(PositionDbContext context);

    Task<NonNegativeDecimalNumber> GetTotalQuoteBalance(NonNegativeDecimalNumber initialQuoteBalance);
    Task<NonNegativeDecimalNumber> GetTotalQuoteBalance(NonNegativeDecimalNumber initialQuoteBalance, PositionDbContext context);

    Task<NonNegativeDecimalNumber> GetTotalRealizedResult();
    Task<NonNegativeDecimalNumber> GetTotalRealizedResult(PositionDbContext context);

    Task<PositionSummary> GetSummary(
        NonNegativeDecimalNumber initialQuoteBalance
    );
    Task<PositionSummary> GetSummary(
        NonNegativeDecimalNumber initialQuoteBalance,
        PositionDbContext context
    );
}