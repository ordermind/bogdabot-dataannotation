namespace Bogdabot.Server.Account.Policies;

public static class PasswordPolicy
{
    public const bool RequireDigit = true;
    public const bool RequireLowercase = true;
    public const bool RequireUppercase = true;
    public const bool RequireNonAlphanumeric = true;
    public const int MinimumLength = 20;
}