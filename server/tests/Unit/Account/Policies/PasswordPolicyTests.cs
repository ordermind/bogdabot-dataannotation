using Bogdabot.Server.Account.Policies;

namespace Bogdabot.Server.Tests.Unit.Account.Policies;

public class PasswordPolicyTests
{
    [Fact]
    public void PasswordPolicy_HasExpectedDefaults()
    {
        Assert.True(PasswordPolicy.RequireDigit);
        Assert.True(PasswordPolicy.RequireLowercase);
        Assert.True(PasswordPolicy.RequireUppercase);
        Assert.True(PasswordPolicy.RequireNonAlphanumeric);
        Assert.Equal(20, PasswordPolicy.MinimumLength);
    }
}
