using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Account.Entities;

public enum UserStatusEnum
{
    Created,
    Active,
    Disabled,
    Archived
}

public class UserEntity : IdentityUser<ulong>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public override ulong Id { get; set; }

    public NonEmptyGuid Guid { get; set; }

    public override string? Email { get; set; }

    public override string? NormalizedEmail { get; set; }

    [PasswordPropertyText]
    public override string? PasswordHash { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }

    public override bool EmailConfirmed { get; set; } = false;

    public UserStatusEnum Status { get; set; } = UserStatusEnum.Created;

    public DateTimeOffset? LastLoginAt { get; set; }

    public ushort FailedLoginCount { get; set; } = 0;

    public DateTimeOffset? LockedUntil { get; set; }

    public override bool TwoFactorEnabled { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
