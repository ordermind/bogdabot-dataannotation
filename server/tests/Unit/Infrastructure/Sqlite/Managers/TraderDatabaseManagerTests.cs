using Bogdabot.Server.Infrastructure.Sqlite.Managers;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Exceptions;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Sqlite.Managers;

public class TraderDatabaseManagerTests
{
    [Theory]
    [InlineData(AppEnvEnum.Test)]
    [InlineData(AppEnvEnum.Production)]
    public void DeleteAllDatabases_ThrowsOutsideDevelopment(AppEnvEnum environment)
    {
        var manager = new TraderDatabaseManager(new EnvironmentVariableService(environment));

        Assert.Throws<IllegalEnvironmentException>(() => manager.DeleteAllDatabases());
    }
}