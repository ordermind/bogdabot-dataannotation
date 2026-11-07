using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Exceptions;
using Bogdabot.Server.Tests.TestSupport;

namespace Bogdabot.Server.Tests.Unit.Shared.Bootstrap;

public class EnvironmentLoaderTests
{
    [Fact]
    public void GetEnvironment_ReturnsDevelopment_WhenVariableIsDevelopment()
    {
        ProcessStateLock.Run(() =>
        {
            var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
                var loader = new EnvironmentLoader();

                var result = loader.GetEnvironment();

                Assert.Equal(AppEnvEnum.Development, result);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
            }
        });
    }

    [Fact]
    public void GetEnvironment_Throws_ForInvalidValue()
    {
        ProcessStateLock.Run(() =>
        {
            var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Invalid");
                var loader = new EnvironmentLoader();

                Assert.Throws<IllegalEnvironmentException>(() => loader.GetEnvironment());
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
            }
        });
    }
}
