namespace Bogdabot.Server.Shared.Config.Database;

public interface IMainDatabaseConfigFactory
{
    public IDatabaseConfig Create();
}