using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Web.Api.V1.ValidationAttributes;

namespace Bogdabot.Server.Tests.Unit.Web.Api.V1.ValidationAttributes;

public class EnumAttributeTests
{
    [Fact]
    public void IsValid_ReturnsTrue_ForMatchingValue_IgnoringCase()
    {
        var sut = new EnumAttribute<BrokerEnum>();

        Assert.True(sut.IsValid("ibkr"));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForInvalidValue()
    {
        var sut = new EnumAttribute<BrokerEnum>();

        Assert.False(sut.IsValid("NotAnExchange"));
    }

    [Fact]
    public void FormatErrorMessage_ContainsAllowedValues()
    {
        var sut = new EnumAttribute<BrokerEnum>();

        var message = sut.FormatErrorMessage("BrokerName");

        Assert.Contains("IBKR", message);
        Assert.Contains("BrokerName", message);
    }
}
