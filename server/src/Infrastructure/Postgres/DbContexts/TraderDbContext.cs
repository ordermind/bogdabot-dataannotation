using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;
using Bogdabot.Server.Trading.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Postgres.DbContexts;

public class TraderDbContext(DbContextOptions<TraderDbContext> options) : DbContext(options)
{
    public DbSet<TraderEntity> Traders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TraderDbContext).Assembly);

        modelBuilder.ConfigureSingleValueObjects();
        modelBuilder.ConfigureEnumConversion();
    }
}
