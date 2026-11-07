using Bogdabot.Server.Shared.Enums;
using Serilog;

namespace Bogdabot.Server.Shared.Debug;

class LoggerFactory(AppEnvEnum environment, string logPath)
{
    private AppEnvEnum Environment { get; } = environment;

    private string LogPath { get; } = logPath;

    public Serilog.ILogger CreateLogger()
    {
        if (AppEnvEnum.Development == this.Environment)
        {
            return this.CreateLoggerDevelopment();
        }
        if (AppEnvEnum.Test == this.Environment)
        {
            return this.CreateLoggerTest();
        }
        if (AppEnvEnum.Production == this.Environment)
        {
            return this.CreateLoggerProduction();
        }

        throw new NotSupportedException($"No logger could be created for environment \"{this.Environment}\"");
    }

    private Serilog.ILogger CreateLoggerDevelopment()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();
    }

    private Serilog.ILogger CreateLoggerTest()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(this.LogPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    private Serilog.ILogger CreateLoggerProduction()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(this.LogPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
}
