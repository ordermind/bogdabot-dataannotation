using Bogdabot.Server.Infrastructure.Postgres.Config;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Tests.TestSupport;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Postgres.Config;

public class PostgresDatabaseConfigFactoryTests
{
    [Fact]
    public void Create_ReturnsPostgresConfig_WhenBackendIsPostgreSql()
    {
        ProcessStateLock.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var originalCwd = Directory.GetCurrentDirectory();

            try
            {
                File.WriteAllText(
                    Path.Combine(tempDir, ".env.test"),
                    "DB_MAIN_BACKEND=PostgreSQL\nDB_MAIN_HOST=localhost\nDB_MAIN_PORT=5432\nDB_MAIN_DATABASE=bogdabot\nDB_MAIN_USERNAME=postgres\nDB_MAIN_PASSWORD=secret");

                Directory.SetCurrentDirectory(tempDir);

                var envService = new EnvironmentVariableService(AppEnvEnum.Test);
                var sut = new PostgresDatabaseConfigFactory(envService);

                var config = sut.Create();

                Assert.Equal(DatabaseStorageTypeEnum.PostgreSQL, config.GetStorageType());
                Assert.Contains("Host=localhost", config.ToMigrationString());
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public void Create_Throws_WhenBackendIsNotSupported()
    {
        ProcessStateLock.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var originalCwd = Directory.GetCurrentDirectory();

            try
            {
                File.WriteAllText(
                    Path.Combine(tempDir, ".env.test"),
                    "DB_MAIN_BACKEND=SQLite\nDB_MAIN_HOST=localhost\nDB_MAIN_PORT=5432\nDB_MAIN_DATABASE=bogdabot\nDB_MAIN_USERNAME=postgres\nDB_MAIN_PASSWORD=secret");

                Directory.SetCurrentDirectory(tempDir);

                var envService = new EnvironmentVariableService(AppEnvEnum.Test);
                var sut = new PostgresDatabaseConfigFactory(envService);

                Assert.Throws<NotSupportedException>(() => sut.Create());
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }
}
