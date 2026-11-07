using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Exceptions;

namespace Bogdabot.Server.Infrastructure.Sqlite.Managers;

public class TraderDatabaseManager(EnvironmentVariableService envService) : ITraderDatabaseManager
{
    private EnvironmentVariableService EnvService { get; } = envService;
    public void DeleteAllDatabases()
    {
        var currentEnvironment = EnvService.GetEnvironment();
        if (currentEnvironment != AppEnvEnum.Development)
        {
            throw new IllegalEnvironmentException($"EnvironmentVariableService.DeleteAllDatabases can only be called in a development environment. Current environment is {currentEnvironment}.");
        }

        var dbFolder = EnvService.GetEnvironmentVariable("DB_TRADER_ROOT_FOLDER");

        if (dbFolder is null)
        {
            throw new MissingEnvironmentVariableException("Error deleting all databases: The environment variable DB_TRADER_ROOT_FOLDER could not be found.");
        }

        if (!Directory.Exists(dbFolder))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(dbFolder))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }
    public void DeleteDatabase(IDatabaseConfig databaseConfig)
    {
        if (databaseConfig.GetStorageType() != DatabaseStorageTypeEnum.SQLite)
        {
            throw new ArgumentException($"The passed storage type is {databaseConfig.GetStorageType()}, but only SQLite is supported.");
        }

        File.Delete(databaseConfig.ToString());
    }
}