using System.ComponentModel.DataAnnotations;
using Bogdabot.Server.Shared.Helpers;

namespace Bogdabot.Server.Web.Api.V1.ValidationAttributes;

public class EnumAttribute<TEnum> : ValidationAttribute
    where TEnum : struct, Enum
{
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        return Enum.TryParse<TEnum>(value.ToString()!, true, out _);
    }

    public override string FormatErrorMessage(string name)
    {
        var values = EnumHelper.GetValues<TEnum>();
        var allowedValues = string.Join(", ", values.Select(v => v.ToString()));

        return $"The value for {name} must be one of: {allowedValues}";
    }
}