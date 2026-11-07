using Bogdabot.Server.Shared.Config.Database;

namespace Bogdabot.Server.Shared.Database;

public interface ITraderDbMigrationService
{
    /// <summary>
    /// Runs all pending migrations for one trader.
    /// </summary>
    Task RunMigrations(IDatabaseConfig databaseConfig);
}