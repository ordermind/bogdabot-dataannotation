using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bogdabot.Server.Infrastructure.Sqlite.Converters;

public sealed class NullableUtcDateTimeOffsetConverter
    : ValueConverter<DateTimeOffset?, DateTime?>
{
    public NullableUtcDateTimeOffsetConverter()
        : base(
            v => v.HasValue ? v.Value.UtcDateTime : null,
            v => v.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                : null)
    { }
}