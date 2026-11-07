using Bogdabot.Server.Web.Api.V1.ValidationAttributes;

namespace Bogdabot.Server.Tests.Unit.Web.Api.V1.ValidationAttributes;

public class PositiveNumberAttributeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(0.01)]
    public void IsValid_ReturnsTrue_ForPositiveNumbers(object value)
    {
        var sut = new PositiveNumberAttribute();

        Assert.True(sut.IsValid(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void IsValid_ReturnsFalse_ForZeroOrNegative(object value)
    {
        var sut = new PositiveNumberAttribute();

        Assert.False(sut.IsValid(value));
    }

    [Fact]
    public void IsValid_ReturnsTrue_ForNull()
    {
        var sut = new PositiveNumberAttribute();

        Assert.True(sut.IsValid(null));
    }
}
