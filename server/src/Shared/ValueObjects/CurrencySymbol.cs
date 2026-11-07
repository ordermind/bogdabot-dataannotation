namespace Bogdabot.Server.Shared.ValueObjects;

public record CurrencySymbol : ISingleValueObject<string, CurrencySymbol>
{
    public string Value { get; }

    public CurrencySymbol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, 100);

        Value = value;
    }

    public static CurrencySymbol FromValue(string value)
    => new(value);
}
