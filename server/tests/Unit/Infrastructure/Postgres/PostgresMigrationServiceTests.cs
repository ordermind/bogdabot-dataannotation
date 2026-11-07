using Bogdabot.Server.Infrastructure.Postgres;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Exceptions;
using Bogdabot.Server.Shared.Users;
using FluentMigrator.Runner;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Postgres;

public class PostgresMigrationServiceTests
{
    [Fact]
    public void RunMigrations_CallsMigrateUp_WhenThereArePendingMigrations()
    {
        var migrationRunner = Substitute.For<IMigrationRunner>();
        migrationRunner.HasMigrationsToApplyUp().Returns(true);

        var sut = CreateService(migrationRunner);

        sut.RunMigrations();

        migrationRunner.Received(1).MigrateUp();
    }

    [Fact]
    public void RunMigrations_DoesNotCallMigrateUp_WhenNoPendingMigrations()
    {
        var migrationRunner = Substitute.For<IMigrationRunner>();
        migrationRunner.HasMigrationsToApplyUp().Returns(false);

        var sut = CreateService(migrationRunner);

        sut.RunMigrations();

        migrationRunner.DidNotReceive().MigrateUp();
    }

    [Theory]
    [InlineData(AppEnvEnum.Test)]
    [InlineData(AppEnvEnum.Production)]
    public async Task MigrateFresh_ThrowsOutsideDevelopment(AppEnvEnum environment)
    {
        var configFactory = Substitute.For<IMainDatabaseConfigFactory>();
        var sut = CreateService(Substitute.For<IMigrationRunner>(), configFactory, environment);

        await Assert.ThrowsAsync<IllegalEnvironmentException>(() => sut.MigrateFresh());

        configFactory.DidNotReceive().Create();
    }

    [Fact]
    public async Task MigrateFresh_Throws_WhenSchemaOwnerCannotBeDetermined()
    {
        var migrationRunner = Substitute.For<IMigrationRunner>();
        var configFactory = Substitute.For<IMainDatabaseConfigFactory>();
        var dbConfig = Substitute.For<IDatabaseConfig>();
        dbConfig.ToMigrationString().Returns("Host=fake;Database=fake;");
        configFactory.Create().Returns(dbConfig);

        var fakeConnection = new FakeDbConnection([null], []);
        var superadminGenerator = Substitute.For<IGeneratesSuperadminUsers>();

        var sut = new TestablePostgresMigrationService(
            configFactory,
            NullLogger<PostgresMigrationService>.Instance,
            migrationRunner,
            superadminGenerator,
            fakeConnection,
            new EnvironmentVariableService(AppEnvEnum.Development));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.MigrateFresh());
        await superadminGenerator.DidNotReceive().CreateSuperadminUser();
    }

    [Fact]
    public async Task MigrateFresh_RunsDropCreateMigrationsAndCreatesSuperadmin()
    {
        var migrationRunner = Substitute.For<IMigrationRunner>();
        migrationRunner.HasMigrationsToApplyUp().Returns(true);

        var configFactory = Substitute.For<IMainDatabaseConfigFactory>();
        var dbConfig = Substitute.For<IDatabaseConfig>();
        dbConfig.ToMigrationString().Returns("Host=fake;Database=fake;");
        configFactory.Create().Returns(dbConfig);

        var fakeConnection = new FakeDbConnection(["postgres"], [1, 1]);
        var superadminGenerator = Substitute.For<IGeneratesSuperadminUsers>();

        var sut = new TestablePostgresMigrationService(
            configFactory,
            NullLogger<PostgresMigrationService>.Instance,
            migrationRunner,
            superadminGenerator,
            fakeConnection,
            new EnvironmentVariableService(AppEnvEnum.Development));

        await sut.MigrateFresh();

        Assert.Equal(
            [
                "SELECT schema_owner FROM information_schema.schemata\nWHERE schema_name = @schema",
                "DROP SCHEMA public CASCADE",
                "CREATE SCHEMA public AUTHORIZATION postgres"
            ],
            fakeConnection.ExecutedCommands);

        migrationRunner.Received(1).MigrateUp();
        await superadminGenerator.Received(1).CreateSuperadminUser();
    }

    private static PostgresMigrationService CreateService(
        IMigrationRunner migrationRunner,
        IMainDatabaseConfigFactory? configFactory = null,
        AppEnvEnum environment = AppEnvEnum.Test)
    {
        configFactory ??= Substitute.For<IMainDatabaseConfigFactory>();
        var logger = NullLogger<PostgresMigrationService>.Instance;
        var superadminGenerator = Substitute.For<IGeneratesSuperadminUsers>();

        return new PostgresMigrationService(
            configFactory,
            logger,
            migrationRunner,
            superadminGenerator,
            new EnvironmentVariableService(environment));
    }

    private sealed class TestablePostgresMigrationService(
        IMainDatabaseConfigFactory databaseConfigFactory,
        ILogger<PostgresMigrationService> logger,
        IMigrationRunner migrationRunner,
        IGeneratesSuperadminUsers superadminGenerator,
        DbConnection connection,
        EnvironmentVariableService envService)
        : PostgresMigrationService(databaseConfigFactory, logger, migrationRunner, superadminGenerator, envService)
    {
        protected override DbConnection CreateConnection(string connectionString)
        {
            return connection;
        }
    }

    private sealed class FakeDbConnection(object?[] scalarResults, int[] nonQueryResults) : DbConnection
    {
        private readonly Queue<object?> _scalarResults = new(scalarResults);
        private readonly Queue<int> _nonQueryResults = new(nonQueryResults);

        public List<string> ExecutedCommands { get; } = [];

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
            => new FakeDbCommand(this, _scalarResults, _nonQueryResults, ExecutedCommands);
    }

    private sealed class FakeDbCommand(
        DbConnection connection,
        Queue<object?> scalarResults,
        Queue<int> nonQueryResults,
        List<string> executedCommands) : DbCommand
    {
        private readonly FakeDbParameterCollection _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        [AllowNull]
        protected override DbConnection DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery()
        {
            executedCommands.Add(CommandText.Trim());
            return nonQueryResults.Count > 0 ? nonQueryResults.Dequeue() : 0;
        }

        public override object? ExecuteScalar()
        {
            executedCommands.Add(CommandText.Trim());
            return scalarResults.Count > 0 ? scalarResults.Dequeue() : null;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => Task.FromResult(ExecuteNonQuery());

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            => Task.FromResult(ExecuteScalar());

        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;
        public override object SyncRoot => this;
        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _parameters.Clear();
        public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        protected override DbParameter GetParameter(string parameterName) => _parameters.Single(p => p.ParameterName == parameterName);
        protected override DbParameter GetParameter(int index) => _parameters[index];
        public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);
        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _parameters.Remove((DbParameter)value);
        public override void RemoveAt(string parameterName) => _parameters.RemoveAt(IndexOf(parameterName));
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var idx = IndexOf(parameterName);
            if (idx >= 0)
            {
                _parameters[idx] = value;
            }
            else
            {
                _parameters.Add(value);
            }
        }

        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    }
}
