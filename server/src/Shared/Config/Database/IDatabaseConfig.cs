using Bogdabot.Server.Shared.Enums;

namespace Bogdabot.Server.Shared.Config.Database;

public interface IDatabaseConfig
{
    public DatabaseStorageTypeEnum GetStorageType();
    public string ToEfString();
    public string ToMigrationString();
    public string ToString();
}
