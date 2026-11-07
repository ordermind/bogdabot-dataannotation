using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Infrastructure.Sqlite.Config;

class SqliteDatabaseConfigFactory(EnvironmentVariableService envService) : ITraderDatabaseConfigFactory
{
    private readonly EnvironmentVariableService envService = envService;

    public IDatabaseConfig Create(NonEmptyGuid userGuid, string traderId)
    {
        string strBackend = this.envService.GetEnvironmentVariable("DB_TRADER_BACKEND");
        var backend = Enum.Parse<DatabaseStorageTypeEnum>(strBackend);
        if (DatabaseStorageTypeEnum.SQLite != backend)
        {
            throw new NotSupportedException($"The database backend \"{strBackend}\" is not supported.");
        }

        bool inMemory = bool.Parse(this.envService.GetEnvironmentVariable("DB_TRADER_IN_MEMORY"));

        if (inMemory)
        {
            return new SqliteDatabaseConfig(":memory:");
        }

        string rootFolder = this.envService.GetEnvironmentVariable("DB_TRADER_ROOT_FOLDER");
        string dbFolder = $"{rootFolder}/user-{userGuid}";
        string filePath = $"{dbFolder}/{traderId}.db";

        System.IO.Directory.CreateDirectory(dbFolder);

        return new SqliteDatabaseConfig(filePath);
    }
}
