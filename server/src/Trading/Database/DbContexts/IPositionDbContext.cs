using Bogdabot.Server.Trading.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Shared.Database.DbContexts;

public interface IPositionDbContext
{
    DbSet<PositionEntity> Positions { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    void Dispose();
}
