using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Postgres.Stores;

public class UserStore(UserDbContext context) :
    IUserStore<UserEntity>,
    IUserPasswordStore<UserEntity>,
    IUserEmailStore<UserEntity>,
    IUserSecurityStampStore<UserEntity>,
    IUserTwoFactorStore<UserEntity>,
    IQueryableUserStore<UserEntity>
{
    private UserDbContext Context { get; } = context;

    public IQueryable<UserEntity> Users => this.Context.Users;

    public async Task<IdentityResult> CreateAsync(UserEntity user, CancellationToken cancellationToken)
    {
        user.Guid = Guid.CreateVersion7();
        user.CreatedAt = DateTimeOffset.UtcNow;
        this.Context.Users.Add(user);

        return await this.SaveChangesAsync(cancellationToken);
    }

    public async Task<IdentityResult> UpdateAsync(UserEntity user, CancellationToken cancellationToken)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        this.Context.Users.Update(user);

        return await this.SaveChangesAsync(cancellationToken);
    }

    public async Task<IdentityResult> DeleteAsync(UserEntity user, CancellationToken cancellationToken)
    {
        this.Context.Users.Remove(user);

        return await this.SaveChangesAsync(cancellationToken);
    }

    public Task<UserEntity?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(userId, out var id))
        {
            return Task.FromResult<UserEntity?>(null);
        }

        return this.Context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public Task<UserEntity?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        return this.Context.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);
    }

    public Task<string> GetUserIdAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult<string>(user.Id.ToString());
    }

    public Task<string?> GetUserNameAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(user.UserName);
    }

    public Task SetUserNameAsync(UserEntity user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(user.NormalizedUserName);
    }

    public Task SetNormalizedUserNameAsync(UserEntity user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(UserEntity user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(user.PasswordHash);
    }

    public Task<bool> HasPasswordAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(user.PasswordHash));
    }

    public Task SetEmailAsync(UserEntity user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(user.Email);
    }

    public Task<bool> GetEmailConfirmedAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.EmailConfirmed);
    }

    public Task SetEmailConfirmedAsync(UserEntity user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<UserEntity?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        return this.Context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public Task<string?> GetNormalizedEmailAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(user.NormalizedEmail);
    }

    public Task SetNormalizedEmailAsync(UserEntity user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(UserEntity user, string? stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(user.SecurityStamp);
    }

    public Task SetTwoFactorEnabledAsync(UserEntity user, bool enabled, CancellationToken cancellationToken)
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(UserEntity user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.TwoFactorEnabled);
    }

    private async Task<IdentityResult> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.Context.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateException ex)
        {
            return IdentityResult.Failed(new IdentityError { Description = ex.Message });
        }
    }

    public void Dispose()
    {
        this.Context.Dispose();
    }
}
