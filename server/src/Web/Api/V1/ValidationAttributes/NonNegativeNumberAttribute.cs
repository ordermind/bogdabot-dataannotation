using System.ComponentModel.DataAnnotations;

namespace Bogdabot.Server.Web.Api.V1.ValidationAttributes;

public class NonNegativeNumberAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value switch
    {
        null => true,

        byte x => x >= 0,
        sbyte x => x >= 0,
        short x => x >= 0,
        ushort x => x >= 0,
        int x => x >= 0,
        uint x => x >= 0,
        long x => x >= 0,
        ulong x => x >= 0,
        nint x => x >= 0,
        nuint x => x >= 0,

        float x => !float.IsNaN(x) && !float.IsInfinity(x) && x >= 0,
        double x => !double.IsNaN(x) && !double.IsInfinity(x) && x >= 0,
        decimal x => x >= 0,

        _ => false
    };

    public override string FormatErrorMessage(string name)
    {
        return $"The value for {name} must be a non-negative number.";
    }
}