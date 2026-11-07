using Bogdabot.Server.Infrastructure.Sqlite.Config;
using Bogdabot.Server.Shared.Enums;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Sqlite.Config;

public class SqliteDatabaseConfigTests
{
    [Fact]
    public void Returns_Sqlite_StorageType_And_ConnectionStrings()
    {
        var config = new SqliteDatabaseConfig("/tmp/trader.db");

        Assert.Equal(DatabaseStorageTypeEnum.SQLite, config.GetStorageType());
        Assert.Equal("Data Source=/tmp/trader.db", config.ToEfString());
        Assert.Equal(config.ToEfString(), config.ToMigrationString());
    }
}
