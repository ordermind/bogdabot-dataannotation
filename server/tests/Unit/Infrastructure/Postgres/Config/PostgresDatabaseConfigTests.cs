using Bogdabot.Server.Infrastructure.Postgres.Config;
using Bogdabot.Server.Shared.Enums;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Postgres.Config;

public class PostgresDatabaseConfigTests
{
    [Fact]
    public void Methods_ReturnExpectedStorageTypeAndConnectionStrings()
    {
        var sut = new PostgresDatabaseConfig(
            host: "localhost",
            port: "5432",
            name: "bogdabot",
            username: "postgres",
            password: "secret");

        Assert.Equal(DatabaseStorageTypeEnum.PostgreSQL, sut.GetStorageType());

        var expected = "Host=localhost;Port=5432;Database=bogdabot;Username=postgres;Password=secret;";
        Assert.Equal(expected, sut.ToEfString());
        Assert.Equal(expected, sut.ToMigrationString());
    }
}
