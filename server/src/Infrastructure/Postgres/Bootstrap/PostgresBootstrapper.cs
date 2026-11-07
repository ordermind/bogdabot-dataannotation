using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Shared.Database;

namespace Bogdabot.Server.Infrastructure.Postgres.Bootstrap;

public class PostgresBootstrapper(WebApplication app) : IBootstrapper
{
    private WebApplication App { get; } = app;
    private void DoMigrations()
    {
        using var scope = App.Services.CreateScope();
        var webMigrationService = scope.ServiceProvider.GetRequiredService<IMainDbMigrationService>();
        webMigrationService.RunMigrations();
    }

    public async Task Run()
    {
        this.DoMigrations();
    }
}