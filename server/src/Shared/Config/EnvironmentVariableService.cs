using DotNetEnv;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Exceptions;

namespace Bogdabot.Server.Shared.Config;

public class EnvironmentVariableService(AppEnvEnum environment)
{
    private AppEnvEnum Environment { get; } = environment;
    private bool IsLoaded { get; set; } = false;

    public void LoadEnvironmentVariables()
    {
        if (this.IsLoaded)
        {
            return;
        }

        var tryFiles = new string[] {
                $".env.{this.Environment.ToString().ToLower()}",
                ".env",
            };

        foreach (string filename in tryFiles)
        {
            if (File.Exists(filename))
            {
                Env.Load(filename);

                this.IsLoaded = true;

                return;
            }
        }

        throw new FileNotFoundException($"No environment file could be found for \"{this.Environment}\".");
    }

    public AppEnvEnum GetEnvironment()
    {
        return Environment;
    }

    public string GetEnvironmentVariable(string name)
    {
        this.LoadEnvironmentVariables();

        var value = System.Environment.GetEnvironmentVariable(name);

        if (null == value)
        {
            throw new MissingEnvironmentVariableException($"The environment variable {name} could not be found.");
        }

        return value;
    }
}
