using Bogdabot.Server.Infrastructure.Sqlite.Migrations;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;

namespace Bogdabot.Server.Infrastructure.Sqlite;

internal class SqliteMigrationService(
    ILogger<SqliteMigrationService> logger
) : ITraderDbMigrationService
{
    private readonly ILogger<SqliteMigrationService> _logger = logger;

    protected static IMigrationRunner CreateMigrator(IDatabaseConfig databaseConfig)
    {
        var connectionString = databaseConfig.ToMigrationString();

        var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(CreateOrdersTable).Assembly).For.Migrations())
                .Configure<RunnerOptions>(opt =>
                    {
                        opt.Tags = ["trader"];
                    }
                )
            .BuildServiceProvider();

        return serviceProvider.GetRequiredService<IMigrationRunner>();
    }

    public async Task RunMigrations(IDatabaseConfig databaseConfig)
    {
        var migrator = CreateMigrator(databaseConfig);

        if (migrator.HasMigrationsToApplyUp())
        {
            migrator.MigrateUp();
            return;
        }

        _logger.LogInformation("No pending database migrations for traders.");
    }
}
