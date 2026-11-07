using Bogdabot.Server.Infrastructure.Akka.Bootstrap;
using Bogdabot.Server.Infrastructure.Miscellaneous.Bootstrap;
using Bogdabot.Server.Infrastructure.Postgres.Bootstrap;
using Bogdabot.Server.Infrastructure.Sqlite.Bootstrap;
using Bogdabot.Server.Shared.Bootstrap;

namespace Bogdabot.Server.Infrastructure;

public class InfrastructureBootstrapper(WebApplication app) : IBootstrapper
{
    private WebApplication App { get; } = app;

    public async Task Run()
    {
        var miscBootstrapper = new MiscellaneousBootstrapper();
        await miscBootstrapper.Run();

        var postgresBootstrapper = new PostgresBootstrapper(App);
        await postgresBootstrapper.Run();

        var sqliteBootstrapper = new SqliteBootstrapper();
        await sqliteBootstrapper.Run();

        var akkaBootstrapper = new AkkaBootstrapper();
        await akkaBootstrapper.Run();
    }
}