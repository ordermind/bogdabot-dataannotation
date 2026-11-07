using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Account.Policies;
using Bogdabot.Server.Infrastructure.Miscellaneous.Database;
using Bogdabot.Server.Infrastructure.Postgres.Config;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Infrastructure.Postgres.Migrations;
using Bogdabot.Server.Infrastructure.Postgres.Normalizers;
using Bogdabot.Server.Infrastructure.Postgres.Repositories;
using Bogdabot.Server.Infrastructure.Postgres.Stores;
using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using SharedTraderRepository = Bogdabot.Server.Shared.Trading.ITraderRepository;
using TradingTraderRepository = Bogdabot.Server.Trading.Repositories.ITraderRepository;
using SharedStoppedTraderRepository = Bogdabot.Server.Shared.Trading.IStoppedTraderRepository;
using TradingStoppedTraderRepository = Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository;
using TradingUserRepository = Bogdabot.Server.Trading.Repositories.IUserRepository;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Bogdabot.Server.Trading.Repositories;

namespace Bogdabot.Server.Infrastructure.Postgres.Bootstrap;

class PostgresServiceLoader(WebApplicationBuilder builder, EnvironmentVariableService environmentVariableService) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;

    private EnvironmentVariableService EnvironmentVariableService { get; } = environmentVariableService;

    private void RegisterDbContext<T>() where T : DbContext
    {
        Builder.Services.AddPooledDbContextFactory<T>((serviceProvider, options) =>
        {
            var config = serviceProvider
                .GetRequiredService<IMainDatabaseConfigFactory>()
                .Create();

            options.EnableSensitiveDataLogging();

            serviceProvider
                .GetRequiredService<IDbOptionsFactory>()
                .SetDatabaseOptions(options, config);
        });
    }

    private void AddMainDbServices()
    {
        Builder.Services.AddSingleton<IMainDatabaseConfigFactory>(
            _ => new PostgresDatabaseConfigFactory(this.EnvironmentVariableService)
        );

        var databaseConfig = new PostgresDatabaseConfigFactory(this.EnvironmentVariableService).Create();

        var connectionString = databaseConfig.ToMigrationString();

        Builder.Services.AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(CreateUsersTable).Assembly).For.Migrations())
                .Configure<RunnerOptions>(opt =>
                    {
                        opt.Tags = ["main"];
                    }
                );

        Builder.Services.AddScoped<IMainDbMigrationService, PostgresMigrationService>();

        // User db context
        this.RegisterDbContext<UserDbContext>();

        // Trader db context
        this.RegisterDbContext<TraderDbContext>();

        // Stopped trader db context
        this.RegisterDbContext<StoppedTraderDbContext>();
    }

    private void AddIdentityServices()
    {
        this.Builder.Services.AddIdentityCore<UserEntity>(options =>
        {
            options.Password.RequireDigit = PasswordPolicy.RequireDigit;
            options.Password.RequireLowercase = PasswordPolicy.RequireLowercase;
            options.Password.RequireUppercase = PasswordPolicy.RequireUppercase;
            options.Password.RequireNonAlphanumeric = PasswordPolicy.RequireNonAlphanumeric;
            options.Password.RequiredLength = PasswordPolicy.MinimumLength;
        })
            .AddUserStore<UserStore>()
            .AddDefaultTokenProviders();

        this.Builder.Services.AddSingleton<ILookupNormalizer, LowerInvariantLookupNormalizer>();
    }

    private void AddRepositories()
    {
        Builder.Services.AddSingleton<TradingTraderRepository, TraderRepository>();
        Builder.Services.AddSingleton<SharedTraderRepository, TraderRepository>();
        Builder.Services.AddSingleton<SharedStoppedTraderRepository, StoppedTraderRepository>();
        Builder.Services.AddSingleton<TradingStoppedTraderRepository, StoppedTraderRepository>();
        Builder.Services.AddSingleton<TradingUserRepository, UserRepository>();
    }

    public void AddServices()
    {
        this.AddMainDbServices();
        this.AddIdentityServices();
        this.AddRepositories();
    }
}