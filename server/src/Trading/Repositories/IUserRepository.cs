using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;

namespace Bogdabot.Server.Trading.Repositories;

public interface IUserRepository
{
    Task<IEnumerable<UserEntity>> GetAllUsersWithActiveTraders();
    Task<IEnumerable<UserEntity>> GetAllUsersWithActiveTraders(UserDbContext context, TraderDbContext traderContext);
}