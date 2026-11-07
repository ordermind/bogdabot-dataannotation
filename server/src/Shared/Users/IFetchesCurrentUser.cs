using Bogdabot.Server.Account.Entities;

namespace Bogdabot.Server.Shared.Users;

public interface IFetchesCurrentUser
{
    Task<UserEntity?> GetCurrentUser();
}