using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;
using Bogdabot.Server.Trading.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Postgres.DbContexts;

public class StoppedTraderDbContext(DbContextOptions<StoppedTraderDbContext> options) : DbContext(options)
{
    public DbSet<StoppedTraderEntity> StoppedTraders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StoppedTraderEntity>(entity =>
        {
            entity.ToTable("tradersstopped");
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoppedTraderDbContext).Assembly);

        modelBuilder.ConfigureSingleValueObjects();
        modelBuilder.ConfigureEnumConversion();
    }
}
