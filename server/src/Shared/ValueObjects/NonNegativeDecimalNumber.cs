namespace Bogdabot.Server.Shared.ValueObjects;

public record NonNegativeDecimalNumber : ISingleValueObject<decimal, NonNegativeDecimalNumber>
{
    public decimal Value { get; }

    public NonNegativeDecimalNumber(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be greater than or equal to zero.");
        }

        Value = value;
    }

    public static implicit operator decimal(NonNegativeDecimalNumber v) => v.Value;

    public static implicit operator NonNegativeDecimalNumber(decimal v) => FromValue(v);

    public override string ToString() => Value.ToString();

    public static NonNegativeDecimalNumber FromValue(decimal value)
=> new(value);
}
