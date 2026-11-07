using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;

public static class DbContextFactoryExtensions
{
    public static DbContextOptionsBuilder<T> CreateOptionBuilder<T>(
        this IDbOptionsFactory optionsFactory,
        IDatabaseConfig dbConfig)
        where T : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<T>();

        optionsFactory.SetDatabaseOptions(optionsBuilder, dbConfig);

        return optionsBuilder;
    }
}

public class DatabaseContextFactory : IDbOptionsFactory
{
    public DbContextOptionsBuilder SetDatabaseOptions(
        DbContextOptionsBuilder optionsBuilder,
        IDatabaseConfig dbConfig)
    {
        return dbConfig.GetStorageType() switch
        {
            DatabaseStorageTypeEnum.PostgreSQL =>
                optionsBuilder
                    .UseNpgsql(dbConfig.ToEfString())
                    .UseSnakeCaseNamingConvention(),

            DatabaseStorageTypeEnum.SQLite =>
                optionsBuilder
                    .UseSqlite(dbConfig.ToEfString())
                    .UseSnakeCaseNamingConvention(),

            _ => throw new InvalidOperationException(
                $"Unsupported storage type {dbConfig.GetStorageType()}")
        };
    }
}
