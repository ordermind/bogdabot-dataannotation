using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Trading.Repositories;
using Bogdabot.Server.Trading.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Postgres.Repositories;

public class UserRepository(IDbContextFactory<UserDbContext> userContextFactory, IDbContextFactory<TraderDbContext> traderContextFactory) : IUserRepository
{
    private IDbContextFactory<UserDbContext> UserContextFactory { get; } = userContextFactory;

    private IDbContextFactory<TraderDbContext> TraderContextFactory { get; } = traderContextFactory;

    public async Task<IEnumerable<UserEntity>> GetAllUsersWithActiveTraders()
    {
        await using var userContext = await UserContextFactory.CreateDbContextAsync();

        await using var traderContext = await TraderContextFactory.CreateDbContextAsync();

        return await GetAllUsersWithActiveTraders(userContext, traderContext);
    }

    public async Task<IEnumerable<UserEntity>> GetAllUsersWithActiveTraders(UserDbContext userContext, TraderDbContext traderContext)
    {
        var activeUserIds = await traderContext.Traders
            .Where(trader => trader.Status == TraderStatusEnum.Running)
            .Select(trader => trader.UserId)
            .Distinct()
            .ToListAsync();

        if (activeUserIds.Count == 0)
        {
            return [];
        }

        return await userContext.Users
            .Where(user => activeUserIds.Contains(user.Id))
            .ToListAsync();
    }
}