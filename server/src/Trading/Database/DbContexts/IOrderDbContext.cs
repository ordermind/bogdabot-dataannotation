using Bogdabot.Server.Trading.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Shared.Database.DbContexts;

public interface IOrderDbContext
{
    DbSet<OrderEntity> Orders { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    void Dispose();
}
