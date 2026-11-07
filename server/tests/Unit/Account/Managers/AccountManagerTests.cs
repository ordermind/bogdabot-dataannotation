using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Account.Managers;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Users;
using Bogdabot.Server.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bogdabot.Server.Tests.Unit.Account.Managers;

public class AccountManagerTests
{
    [Fact]
    public async Task CreateSuperadminUser_ReturnsWithoutCreate_WhenUserAlreadyExists()
    {
        await ProcessStateGuard(async () =>
        {
            var tempDir = CreateEnvDir("SUPERADMIN_EMAIL=admin@example.com\nSUPERADMIN_PASSWORD=VerySecurePass123!\n");
            var originalCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tempDir);

            try
            {
                var existing = new UserEntity { NormalizedEmail = "ADMIN@EXAMPLE.COM" };
                var userManager = CreateUserManager(new[] { existing });
                userManager.NormalizeEmail("admin@example.com").Returns("ADMIN@EXAMPLE.COM");
                var manager = CreateAccountManager(userManager);

                await manager.CreateSuperadminUser();

                await userManager.DidNotReceive().CreateAsync(Arg.Any<UserEntity>(), Arg.Any<string>());
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public async Task CreateSuperadminUser_CreatesUser_WhenNotExisting()
    {
        await ProcessStateGuard(async () =>
        {
            var tempDir = CreateEnvDir("SUPERADMIN_EMAIL=admin@example.com\nSUPERADMIN_PASSWORD=VerySecurePass123!\n");
            var originalCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tempDir);

            try
            {
                var userManager = CreateUserManager(Array.Empty<UserEntity>());
                userManager.NormalizeEmail("admin@example.com").Returns("ADMIN@EXAMPLE.COM");
                userManager.CreateAsync(Arg.Any<UserEntity>(), "VerySecurePass123!")
                    .Returns(IdentityResult.Success);

                var manager = CreateAccountManager(userManager);

                await manager.CreateSuperadminUser();

                await userManager.Received(1).CreateAsync(
                    Arg.Is<UserEntity>(u =>
                        u != null &&
                        u.Email == "admin@example.com" &&
                        u.UserName == "admin@example.com" &&
                        u.NormalizedEmail == "ADMIN@EXAMPLE.COM" &&
                        u.NormalizedUserName == "ADMIN@EXAMPLE.COM" &&
                        u.Status == UserStatusEnum.Active),
                    "VerySecurePass123!");
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    [Fact]
    public async Task CreateSuperadminUser_Throws_WhenIdentityCreateFails()
    {
        await ProcessStateGuard(async () =>
        {
            var tempDir = CreateEnvDir("SUPERADMIN_EMAIL=admin@example.com\nSUPERADMIN_PASSWORD=VerySecurePass123!\n");
            var originalCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tempDir);

            try
            {
                var userManager = CreateUserManager(Array.Empty<UserEntity>());
                userManager.NormalizeEmail("admin@example.com").Returns("ADMIN@EXAMPLE.COM");
                userManager.CreateAsync(Arg.Any<UserEntity>(), "VerySecurePass123!")
                    .Returns(IdentityResult.Failed(new IdentityError { Description = "Create failed" }));

                var manager = CreateAccountManager(userManager);

                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateSuperadminUser());
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                Directory.Delete(tempDir, recursive: true);
            }
        });
    }

    private static Task ProcessStateGuard(Func<Task> action)
    {
        ProcessStateLock.Run(() => action().GetAwaiter().GetResult());
        return Task.CompletedTask;
    }

    private static string CreateEnvDir(string envContent)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bogdabot-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, ".env.test"), envContent);

        return tempDir;
    }

    private static IGeneratesSuperadminUsers CreateAccountManager(UserManager<UserEntity> userManager)
    {
        var envService = new EnvironmentVariableService(AppEnvEnum.Test);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(UserManager<UserEntity>)).Returns(userManager);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new AccountManager(envService, scopeFactory);
    }

    private static UserManager<UserEntity> CreateUserManager(IEnumerable<UserEntity> users)
    {
        var queryableStore = Substitute.For<IUserStore<UserEntity>, IQueryableUserStore<UserEntity>>();
        ((IQueryableUserStore<UserEntity>)queryableStore).Users.Returns(users.AsQueryable());

        var options = Options.Create(new IdentityOptions());
        var passwordHasher = Substitute.For<IPasswordHasher<UserEntity>>();
        var userValidators = new List<IUserValidator<UserEntity>>();
        var passwordValidators = new List<IPasswordValidator<UserEntity>>();
        var keyNormalizer = Substitute.For<ILookupNormalizer>();
        var errors = new IdentityErrorDescriber();
        var services = Substitute.For<IServiceProvider>();
        var logger = Substitute.For<ILogger<UserManager<UserEntity>>>();

        return Substitute.ForPartsOf<UserManager<UserEntity>>(
            queryableStore,
            options,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            services,
            logger);
    }
}
