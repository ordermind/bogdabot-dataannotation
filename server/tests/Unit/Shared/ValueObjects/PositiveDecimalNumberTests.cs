using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Tests.Unit.Shared.ValueObjects;

public class PositiveDecimalNumberTests
{
    [Fact]
    public void Constructor_Allows_Positive()
    {
        var number = new PositiveDecimalNumber(10.25m);

        Assert.Equal(10.25m, number.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    public void Constructor_Throws_ForZeroOrNegative(decimal value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositiveDecimalNumber(value));
    }
}
