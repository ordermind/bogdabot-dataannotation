using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Enums;

namespace Bogdabot.Server.Infrastructure.Sqlite.Config;

class SqliteDatabaseConfig(
    string filePath
) : IDatabaseConfig
{
    public string FilePath { get; } = filePath;

    public DatabaseStorageTypeEnum GetStorageType()
    {
        return DatabaseStorageTypeEnum.SQLite;
    }

    public string ToEfString()
    {
        return $"Data Source={this.FilePath}";
    }

    public string ToMigrationString()
    {
        return ToEfString();
    }

    public override string ToString()
    {
        return FilePath;
    }
}