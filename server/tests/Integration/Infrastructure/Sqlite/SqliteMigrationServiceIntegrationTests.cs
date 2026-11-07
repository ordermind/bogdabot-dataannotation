using Bogdabot.Server.Infrastructure.Sqlite;
using Bogdabot.Server.Infrastructure.Sqlite.Config;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Sqlite;

public class SqliteMigrationServiceIntegrationTests
{
    [Fact]
    public async Task RunMigrations_Completes_ForNewDatabaseFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "trader.db");
            var config = new SqliteDatabaseConfig(dbPath);
            var logger = NullLogger<SqliteMigrationService>.Instance;
            var sut = new SqliteMigrationService(logger);

            await sut.RunMigrations(config);

            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

}
