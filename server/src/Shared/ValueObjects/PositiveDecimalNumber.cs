namespace Bogdabot.Server.Shared.ValueObjects;

public record PositiveDecimalNumber : ISingleValueObject<decimal, PositiveDecimalNumber>
{
    public decimal Value { get; }

    public PositiveDecimalNumber(decimal value)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be greater than zero.");
        }

        Value = value;
    }

    public static implicit operator decimal(PositiveDecimalNumber v) => v.Value;

    public static implicit operator PositiveDecimalNumber(decimal v) => FromValue(v);

    public override string ToString() => Value.ToString();

    public static PositiveDecimalNumber FromValue(decimal value)
=> new(value);
}
