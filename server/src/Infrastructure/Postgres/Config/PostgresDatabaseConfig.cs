using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Enums;

namespace Bogdabot.Server.Infrastructure.Postgres.Config;

class PostgresDatabaseConfig(
    string host,
    string port,
    string name,
    string username,
    string password
) : IDatabaseConfig
{
    public string Host { get; } = host;
    public string Port { get; } = port;
    public string Name { get; } = name;
    public string Username { get; } = username;
    public string Password { get; } = password;

    public DatabaseStorageTypeEnum GetStorageType()
    {
        return DatabaseStorageTypeEnum.PostgreSQL;
    }

    public string ToEfString()
    {
        return $"Host={this.Host};Port={this.Port};Database={this.Name};Username={this.Username};Password={this.Password};";
    }

    public string ToMigrationString()
    {
        return $"Host={this.Host};Port={this.Port};Database={this.Name};Username={this.Username};Password={this.Password};";
    }

    public override string ToString()
    {
        return ToEfString();
    }
}
