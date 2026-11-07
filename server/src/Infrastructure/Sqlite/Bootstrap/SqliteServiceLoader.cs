using Bogdabot.Server.Infrastructure.Miscellaneous.Database;
using Bogdabot.Server.Infrastructure.Sqlite.Config;
using Bogdabot.Server.Infrastructure.Sqlite.Factories;
using Bogdabot.Server.Infrastructure.Sqlite.Managers;
using Bogdabot.Server.Infrastructure.Sqlite.Repositories;
using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Sqlite.Bootstrap;

class SqliteServiceLoader(WebApplicationBuilder builder) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;

    public void AddServices()
    {
        Builder.Services.AddSingleton<ITraderDatabaseConfigFactory>(
            serviceProvider => new SqliteDatabaseConfigFactory(serviceProvider.GetRequiredService<EnvironmentVariableService>())
        );

        Builder.Services.AddSingleton<ITraderDbMigrationService, SqliteMigrationService>();

        Builder.Services.AddSingleton<ITraderDatabaseManager, TraderDatabaseManager>();

        Builder.Services.AddSingleton<TraderDbContextFactory>();

        Builder.Services.AddSingleton<IPositionRepositoryFactory, PositionRepositoryFactory>();
    }
}