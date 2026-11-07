namespace Bogdabot.Server.Shared.Users;

public interface IGeneratesSuperadminUsers
{
    public Task CreateSuperadminUser();
}