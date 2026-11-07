using Bogdabot.Server.Infrastructure.Sqlite.Converters;
using Bogdabot.Server.Trading.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bogdabot.Server.Infrastructure.Sqlite.Config;

public class PositionEntityConfiguration : IEntityTypeConfiguration<PositionEntity>
{
    public void Configure(EntityTypeBuilder<PositionEntity> builder)
    {
        builder.Property(x => x.BoughtAt)
            .HasConversion<UtcDateTimeOffsetConverter>();

        builder.Property(x => x.SoldAt)
            .HasConversion<NullableUtcDateTimeOffsetConverter>();
    }
}