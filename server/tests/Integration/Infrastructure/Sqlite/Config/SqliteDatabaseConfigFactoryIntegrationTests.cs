using Bogdabot.Server.Infrastructure.Sqlite.Config;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Shared.Enums;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Sqlite.Config;

public class SqliteDatabaseConfigFactoryIntegrationTests
{
    [Fact]
    public void Create_UsesInMemory_WhenConfigured()
    {
        IntegrationProcessStateGuard.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var originalCwd = Directory.GetCurrentDirectory();

            try
            {
                File.WriteAllText(
                    Path.Combine(tempDir, ".env.test"),
                    "DB_TRADER_BACKEND=SQLite\nDB_TRADER_IN_MEMORY=true\nDB_TRADER_ROOT_FOLDER=/unused");

                Directory.SetCurrentDirectory(tempDir);

                var envService = new EnvironmentVariableService(AppEnvEnum.Test);
                var sut = new SqliteDatabaseConfigFactory(envService);

                var config = sut.Create(NonEmptyGuid.NewGuid(), "actor-1");

                Assert.Equal(DatabaseStorageTypeEnum.SQLite, config.GetStorageType());
                Assert.Equal("Data Source=:memory:", config.ToEfString());
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public void Create_UsesDiskPath_AndCreatesDirectory_WhenConfigured()
    {
        IntegrationProcessStateGuard.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var originalCwd = Directory.GetCurrentDirectory();

            try
            {
                var rootFolder = Path.Combine(tempDir, "trader-db-root");
                File.WriteAllText(
                    Path.Combine(tempDir, ".env.test"),
                    $"DB_TRADER_BACKEND=SQLite\nDB_TRADER_IN_MEMORY=false\nDB_TRADER_ROOT_FOLDER={rootFolder}");

                Directory.SetCurrentDirectory(tempDir);

                var envService = new EnvironmentVariableService(AppEnvEnum.Test);
                var sut = new SqliteDatabaseConfigFactory(envService);
                var userGuid = NonEmptyGuid.NewGuid();

                var config = sut.Create(userGuid, "actor-9");

                var expectedDir = Path.Combine(rootFolder, $"user-{userGuid}");
                Assert.True(Directory.Exists(expectedDir));
                Assert.Equal($"Data Source={Path.Combine(expectedDir, "actor-9.db")}", config.ToEfString());
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }
}
