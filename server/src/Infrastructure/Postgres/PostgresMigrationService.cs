using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Exceptions;
using Bogdabot.Server.Shared.Users;
using FluentMigrator.Runner;
using Npgsql;
using System.Data.Common;

namespace Bogdabot.Server.Infrastructure.Postgres;

internal class PostgresMigrationService(
    IMainDatabaseConfigFactory databaseConfigFactory,
    ILogger<PostgresMigrationService> logger,
    IMigrationRunner migrationRunner,
    IGeneratesSuperadminUsers superadminGenerator,
    EnvironmentVariableService envService
) : IMainDbMigrationService
{
    private readonly IMainDatabaseConfigFactory _databaseConfigFactory = databaseConfigFactory;
    private readonly ILogger<PostgresMigrationService> _logger = logger;
    private readonly IMigrationRunner _migrationRunner = migrationRunner;

    private readonly IGeneratesSuperadminUsers _superadminGenerator = superadminGenerator;

    private readonly EnvironmentVariableService _envService = envService;

    protected virtual DbConnection CreateConnection(string connectionString)
    {
        return new NpgsqlConnection(connectionString);
    }

    public void RunMigrations()
    {
        if (_migrationRunner.HasMigrationsToApplyUp())
        {
            _migrationRunner.MigrateUp();
            return;
        }

        _logger.LogInformation("No pending migrations for the shared database.");
    }

    public async Task MigrateFresh()
    {
        var currentEnvironment = _envService.GetEnvironment();
        if (currentEnvironment != Shared.Enums.AppEnvEnum.Development)
        {
            throw new IllegalEnvironmentException($"Migrate:fresh can only be used in a development environment. Current environment is {currentEnvironment}.");
        }

        var connectionString = _databaseConfigFactory.Create().ToMigrationString();
        await using var connection = CreateConnection(connectionString);
        await connection.OpenAsync();

        await using var getOwnerCommand = connection.CreateCommand();
        getOwnerCommand.CommandText = """
            SELECT schema_owner FROM information_schema.schemata
            WHERE schema_name = @schema
            """;
        var schemaParameter = getOwnerCommand.CreateParameter();
        schemaParameter.ParameterName = "schema";
        schemaParameter.Value = "public";
        getOwnerCommand.Parameters.Add(schemaParameter);

        var ownerResult = await getOwnerCommand.ExecuteScalarAsync();

        string owner = ownerResult as string
            ?? throw new InvalidOperationException("Could not determine owner of public schema.");

        var dropSchemaCommand = connection.CreateCommand();
        dropSchemaCommand.CommandText = "DROP SCHEMA public CASCADE";
        await dropSchemaCommand.ExecuteNonQueryAsync();

        var createSchemaCommand = connection.CreateCommand();
        createSchemaCommand.CommandText = $"CREATE SCHEMA public AUTHORIZATION {owner}";
        await createSchemaCommand.ExecuteNonQueryAsync();

        RunMigrations();

        await _superadminGenerator.CreateSuperadminUser();
    }
}
