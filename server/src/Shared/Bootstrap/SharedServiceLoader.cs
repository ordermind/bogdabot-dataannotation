using Bogdabot.Server.Shared.Config;
using Serilog;

namespace Bogdabot.Server.Shared.Bootstrap;

class SharedServiceLoader(WebApplicationBuilder builder) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;
    public EnvironmentVariableService? EnvironmentVariableService { get; private set; }

    public void AddServices()
    {
        var environment = new EnvironmentLoader().GetEnvironment();
        this.EnvironmentVariableService = new EnvironmentVariableService(environment);
        Builder.Services.AddSingleton<EnvironmentVariableService>(this.EnvironmentVariableService);

        var loggerFactory = new Bogdabot.Server.Shared.Debug.LoggerFactory(environment, this.EnvironmentVariableService.GetEnvironmentVariable("LOG_PATH"));
        Builder.Logging.ClearProviders();
        Builder.Logging.AddSerilog(loggerFactory.CreateLogger());
    }
}