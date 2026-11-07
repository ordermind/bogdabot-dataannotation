using Bogdabot.Server.Shared.Config.Database;

namespace Bogdabot.Server.Shared.Database;

public interface ITraderDatabaseManager
{
    public void DeleteAllDatabases();
    public void DeleteDatabase(IDatabaseConfig databaseConfig);
}
