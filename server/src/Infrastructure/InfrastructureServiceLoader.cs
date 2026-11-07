using Bogdabot.Server.Infrastructure.Akka.Bootstrap;
using Bogdabot.Server.Infrastructure.Miscellaneous.Bootstrap;
using Bogdabot.Server.Infrastructure.Postgres.Bootstrap;
using Bogdabot.Server.Infrastructure.Sqlite.Bootstrap;
using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Shared.Config;

namespace Bogdabot.Server.Infrastructure;

class InfrastructureServiceLoader(WebApplicationBuilder builder, EnvironmentVariableService environmentVariableService) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;
    private EnvironmentVariableService EnvironmentVariableService { get; } = environmentVariableService;

    public void AddServices()
    {
        var miscServiceLoader = new MiscellaneousServiceLoader(Builder);
        miscServiceLoader.AddServices();

        var postgresServiceLoader = new PostgresServiceLoader(Builder, EnvironmentVariableService);
        postgresServiceLoader.AddServices();

        var sqliteServiceLoader = new SqliteServiceLoader(Builder);
        sqliteServiceLoader.AddServices();

        var akkaServiceLoader = new AkkaServiceLoader(Builder);
        akkaServiceLoader.AddServices();

        this.Builder.Services.AddDataProtection();
    }
}