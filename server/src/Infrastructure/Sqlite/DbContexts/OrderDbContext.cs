using Microsoft.EntityFrameworkCore;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Shared.Database.DbContexts;
using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;

namespace Bogdabot.Server.Infrastructure.Sqlite.DbContexts;

class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options), IOrderDbContext
{
    public DbSet<OrderEntity> Orders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);

        modelBuilder.ConfigureSingleValueObjects();
        modelBuilder.ConfigureEnumConversion();
    }
}
