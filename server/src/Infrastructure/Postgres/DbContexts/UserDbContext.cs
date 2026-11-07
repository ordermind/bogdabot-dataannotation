using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Postgres.DbContexts;

public class UserDbContext(DbContextOptions<UserDbContext> options)
    : IdentityDbContext<UserEntity, IdentityRole<ulong>, ulong>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Guid).HasColumnName("guid");
            entity.Property(e => e.UserName).HasColumnName("username");
            entity.Property(e => e.NormalizedUserName).HasColumnName("normalized_username");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.NormalizedEmail).HasColumnName("normalized_email");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.EmailConfirmed).HasColumnName("email_confirmed");
            entity.Property(e => e.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            entity.Property(e => e.PasswordChangedAt).HasColumnName("password_changed_at");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.FailedLoginCount).HasColumnName("failed_login_count");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.Status).HasColumnName("status");

            entity.Ignore(e => e.PhoneNumber);
            entity.Ignore(e => e.PhoneNumberConfirmed);
            entity.Ignore(e => e.LockoutEnabled);
            entity.Ignore(e => e.LockoutEnd);
            entity.Ignore(e => e.AccessFailedCount);
            entity.Ignore(e => e.SecurityStamp);
            entity.Ignore(e => e.ConcurrencyStamp);
        });

        modelBuilder.Ignore<IdentityUserClaim<ulong>>();
        modelBuilder.Ignore<IdentityUserLogin<ulong>>();
        modelBuilder.Ignore<IdentityUserToken<ulong>>();
        modelBuilder.Ignore<IdentityRole<ulong>>();
        modelBuilder.Ignore<IdentityUserRole<ulong>>();

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserDbContext).Assembly);

        modelBuilder.ConfigureSingleValueObjects();
        modelBuilder.ConfigureEnumConversion();
    }
}
