using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Config;

namespace Bogdabot.Server.Infrastructure.Postgres.Config;

class PostgresDatabaseConfigFactory(EnvironmentVariableService envService) : IMainDatabaseConfigFactory
{
    private readonly EnvironmentVariableService envService = envService;

    public IDatabaseConfig Create()
    {
        var strBackend = this.envService.GetEnvironmentVariable("DB_MAIN_BACKEND");
        var backend = Enum.Parse<DatabaseStorageTypeEnum>(strBackend);
        if (DatabaseStorageTypeEnum.PostgreSQL == backend)
        {
            return this.CreatePostgresConfig();
        }

        throw new NotSupportedException($"The database backend \"{strBackend}\" is not supported.");
    }

    private PostgresDatabaseConfig CreatePostgresConfig()
    {
        return new PostgresDatabaseConfig(
            this.envService.GetEnvironmentVariable("DB_MAIN_HOST"),
            this.envService.GetEnvironmentVariable("DB_MAIN_PORT"),
            this.envService.GetEnvironmentVariable("DB_MAIN_DATABASE"),
            this.envService.GetEnvironmentVariable("DB_MAIN_USERNAME"),
            this.envService.GetEnvironmentVariable("DB_MAIN_PASSWORD")
        );
    }
}
