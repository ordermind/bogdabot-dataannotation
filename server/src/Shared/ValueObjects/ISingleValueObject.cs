namespace Bogdabot.Server.Shared.ValueObjects;

public interface ISingleValueObject<TPrimitive, TSelf>
    where TSelf : ISingleValueObject<TPrimitive, TSelf>
{
    TPrimitive Value { get; }

    static abstract TSelf FromValue(TPrimitive value);
}