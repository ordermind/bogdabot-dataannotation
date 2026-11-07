using Microsoft.EntityFrameworkCore;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Shared.Database.DbContexts;
using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;

namespace Bogdabot.Server.Infrastructure.Sqlite.DbContexts;

public class PositionDbContext(DbContextOptions<PositionDbContext> options) : DbContext(options), IPositionDbContext
{
    public DbSet<PositionEntity> Positions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PositionDbContext).Assembly);

        modelBuilder.ConfigureSingleValueObjects();
        modelBuilder.ConfigureEnumConversion();
    }
}
