using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Trading.DTO;

public record PositionSummary
{
    public required NonNegativeDecimalNumber CurrentBaseBalance { get; init; }
    public required NonNegativeDecimalNumber CurrentQuoteBalance { get; init; }
    public required NonNegativeDecimalNumber RealizedResult { get; init; }
}