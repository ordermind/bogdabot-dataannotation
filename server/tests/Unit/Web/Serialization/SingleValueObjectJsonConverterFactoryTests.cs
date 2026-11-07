using System.Text.Json;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Web.Serialization;

namespace Bogdabot.Server.Tests.Unit.Web.Serialization;

public class SingleValueObjectJsonConverterFactoryTests
{
    [Fact]
    public void CanConvert_ReturnsTrue_ForSingleValueObjectType()
    {
        var sut = new SingleValueObjectJsonConverterFactory();

        var result = sut.CanConvert(typeof(CurrencySymbol));

        Assert.True(result);
    }

    [Fact]
    public void CanConvert_ReturnsFalse_ForPrimitiveType()
    {
        var sut = new SingleValueObjectJsonConverterFactory();

        var result = sut.CanConvert(typeof(int));

        Assert.False(result);
    }

    [Fact]
    public void CreateConverter_AndRoundTrip_WorksForCurrencySymbol()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SingleValueObjectJsonConverterFactory());

        var model = new Holder
        {
            Symbol = new CurrencySymbol("BTC")
        };

        var json = JsonSerializer.Serialize(model, options);
        var deserialized = JsonSerializer.Deserialize<Holder>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("BTC", deserialized!.Symbol.Value);
    }

    private sealed class Holder
    {
        public required CurrencySymbol Symbol { get; init; }
    }
}
