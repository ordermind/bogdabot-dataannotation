using Bogdabot.Server.Shared.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bogdabot.Server.Infrastructure.Miscellaneous.Database.Configurations;

public class SingleValueObjectConverter<TSelf, TPrimitive>
    : ValueConverter<TSelf, TPrimitive>
    where TSelf : ISingleValueObject<TPrimitive, TSelf>
{
    public SingleValueObjectConverter()
        : base(
            valueObject => valueObject.Value,
            value => Create(value))
    { }

    private static TSelf Create(TPrimitive value)
    {
        return TSelf.FromValue(value);
    }
}