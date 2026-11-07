using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bogdabot.Server.Infrastructure.Sqlite.Converters;

public sealed class UtcDateTimeOffsetConverter
    : ValueConverter<DateTimeOffset, DateTime>
{
    public UtcDateTimeOffsetConverter()
        : base(
            v => v.UtcDateTime,
            v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)))
    { }
}