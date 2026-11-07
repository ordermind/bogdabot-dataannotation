using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Tests.Unit.Shared.ValueObjects;

public class CurrencySymbolTests
{
    [Fact]
    public void Constructor_SetsValue()
    {
        var symbol = new CurrencySymbol("BTC");

        Assert.Equal("BTC", symbol.Value);
    }

    [Fact]
    public void Constructor_Throws_ForWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new CurrencySymbol(" "));
    }

    [Fact]
    public void Constructor_Throws_ForTooLongValue()
    {
        var tooLong = new string('A', 101);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CurrencySymbol(tooLong));
    }
}
