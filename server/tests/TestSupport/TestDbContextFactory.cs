using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Tests.TestSupport;

internal sealed class TestDbContextFactory<TContext>(
    DbContextOptions<TContext> options,
    Func<DbContextOptions<TContext>, TContext> createContext
) : IDbContextFactory<TContext> where TContext : DbContext
{
    public TContext CreateDbContext() => createContext(options);

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}