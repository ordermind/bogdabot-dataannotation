using Bogdabot.Server.Infrastructure.Sqlite.Converters;
using Bogdabot.Server.Trading.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bogdabot.Server.Infrastructure.Sqlite.Config;

public class OrderEntityConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.Property(x => x.OpenedAt)
            .HasConversion<UtcDateTimeOffsetConverter>();

        builder.Property(x => x.ClosedAt)
            .HasConversion<NullableUtcDateTimeOffsetConverter>();
    }
}