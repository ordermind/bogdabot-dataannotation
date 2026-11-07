using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Tests.Unit.Shared.ValueObjects;

public class NonNegativeDecimalNumberTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    public void Constructor_Allows_Zero_And_Positive(decimal value)
    {
        var number = new NonNegativeDecimalNumber(value);

        Assert.Equal(value, number.Value);
    }

    [Fact]
    public void Constructor_Throws_ForNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NonNegativeDecimalNumber(-0.01m));
    }
}
