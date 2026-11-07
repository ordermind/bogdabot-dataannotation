using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Exceptions;

namespace Bogdabot.Server.Tests.Integration.Shared.Config;

public class EnvironmentVariableServiceIntegrationTests
{
    [Fact]
    public void GetEnvironmentVariable_ReadsValueFromEnvFile()
    {
        IntegrationProcessStateGuard.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var originalCwd = Directory.GetCurrentDirectory();

            try
            {
                File.WriteAllText(Path.Combine(tempDir, ".env.test"), "TEST_SAMPLE_KEY=test-value");
                Directory.SetCurrentDirectory(tempDir);

                var service = new EnvironmentVariableService(AppEnvEnum.Test);

                var value = service.GetEnvironmentVariable("TEST_SAMPLE_KEY");

                Assert.Equal("test-value", value);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public void GetEnvironmentVariable_ThrowsForMissingVariable()
    {
        IntegrationProcessStateGuard.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var originalCwd = Directory.GetCurrentDirectory();

            try
            {
                File.WriteAllText(Path.Combine(tempDir, ".env.test"), "OTHER_KEY=other-value");
                Directory.SetCurrentDirectory(tempDir);

                var service = new EnvironmentVariableService(AppEnvEnum.Test);

                Assert.Throws<MissingEnvironmentVariableException>(() => service.GetEnvironmentVariable("DOES_NOT_EXIST"));
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }
}
