using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Exceptions;

namespace Bogdabot.Server.Shared.Bootstrap;

class EnvironmentLoader
{
    public AppEnvEnum GetEnvironment()
    {
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") switch
        {
            "Development" => AppEnvEnum.Development,
            "Test" => AppEnvEnum.Test,
            "Production" => AppEnvEnum.Production,
            _ => throw new IllegalEnvironmentException($"An illegal environment was used. The allowed environments are: {String.Join(", ", Enum.GetNames(typeof(AppEnvEnum)))}")
        };
    }
}
