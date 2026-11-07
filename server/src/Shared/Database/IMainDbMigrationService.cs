namespace Bogdabot.Server.Shared.Database;

public interface IMainDbMigrationService
{
    /// <summary>
    /// Runs all pending migrations.
    /// </summary>
    void RunMigrations();

    /// <summary>
    /// Recreate the whole database and then run all migrations.
    /// </summary>
    Task MigrateFresh();
}