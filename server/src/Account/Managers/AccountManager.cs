using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Shared.Config;
using Bogdabot.Server.Shared.Users;
using Microsoft.AspNetCore.Identity;

namespace Bogdabot.Server.Account.Managers;

public sealed class AccountManager(
    EnvironmentVariableService envService,
    IServiceScopeFactory scopeFactory
) : IGeneratesSuperadminUsers, IFetchesCurrentUser
{
    private EnvironmentVariableService EnvService { get; } = envService;
    private IServiceScopeFactory ScopeFactory { get; } = scopeFactory;
    public async Task CreateSuperadminUser()
    {
        var email = EnvService.GetEnvironmentVariable("SUPERADMIN_EMAIL");
        var password = EnvService.GetEnvironmentVariable("SUPERADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        using var scope = this.ScopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserEntity>>();

        var normalizedEmail = userManager.NormalizeEmail(email);
        var existingUser = userManager.Users.FirstOrDefault(u => u.NormalizedEmail == normalizedEmail);
        if (existingUser is not null)
        {
            return;
        }

        var user = new UserEntity
        {
            Guid = Guid.CreateVersion7(),
            UserName = email.Trim(),
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            NormalizedUserName = normalizedEmail,
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = UserStatusEnum.Active,
            TwoFactorEnabled = false,
            LockoutEnabled = false,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Failed to create superadmin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }

    public async Task<UserEntity?> GetCurrentUser()
    {
        using var scope = this.ScopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserEntity>>();

        return await userManager.FindByIdAsync("1");
    }
}