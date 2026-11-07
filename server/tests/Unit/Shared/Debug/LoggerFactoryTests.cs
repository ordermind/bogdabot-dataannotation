using Bogdabot.Server.Shared.Debug;
using Bogdabot.Server.Shared.Enums;

namespace Bogdabot.Server.Tests.Unit.Shared.Debug;

public class LoggerFactoryTests
{
    [Fact]
    public void CreateLogger_ReturnsLogger_ForDevelopment()
    {
        var sut = new LoggerFactory(AppEnvEnum.Development, "/tmp/unused.log");

        var logger = sut.CreateLogger();

        Assert.NotNull(logger);
    }

    [Fact]
    public void CreateLogger_WritesToFile_ForTestEnvironment()
    {
        var logDir = Path.Combine(Path.GetTempPath(), $"bogdabot-logger-{Guid.NewGuid()}");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "test.log");

        try
        {
            var sut = new LoggerFactory(AppEnvEnum.Test, logPath);
            var logger = sut.CreateLogger();
            logger.Information("logger test message");
            (logger as IDisposable)?.Dispose();

            var files = Directory.GetFiles(logDir);
            Assert.NotEmpty(files);
        }
        finally
        {
            Directory.Delete(logDir, recursive: true);
        }
    }
}
