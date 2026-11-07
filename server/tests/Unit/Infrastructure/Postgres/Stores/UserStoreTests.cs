using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Bogdabot.Server.Infrastructure.Postgres.Stores;
using Microsoft.EntityFrameworkCore;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Postgres.Stores;

public class UserStoreTests
{
    [Fact]
    public async Task CreateAsync_PersistsAndSetsAuditFields()
    {
        await using var context = CreateContext();
        var sut = new UserStore(context);
        var user = NewUser("user1@example.com", "USER1@EXAMPLE.COM");

        var result = await sut.CreateAsync(user, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotEqual(default, user.CreatedAt);
        Assert.Single(context.Users);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesUserAndSetsUpdatedAt()
    {
        await using var context = CreateContext();
        var user = NewUser("user2@example.com", "USER2@EXAMPLE.COM");
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new UserStore(context);
        user.UserName = "renamed@example.com";

        var result = await sut.UpdateAsync(user, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(user.UpdatedAt);
        Assert.Equal("renamed@example.com", (await context.Users.SingleAsync(TestContext.Current.CancellationToken)).UserName);
    }

    [Fact]
    public async Task DeleteAsync_RemovesUser()
    {
        await using var context = CreateContext();
        var user = NewUser("user3@example.com", "USER3@EXAMPLE.COM");
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new UserStore(context);
        var result = await sut.DeleteAsync(user, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(context.Users);
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNull_ForInvalidId()
    {
        await using var context = CreateContext();
        var sut = new UserStore(context);

        var found = await sut.FindByIdAsync("not-a-number", TestContext.Current.CancellationToken);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindByIdAndNameAndEmail_Works()
    {
        await using var context = CreateContext();
        var user = NewUser("user4@example.com", "USER4@EXAMPLE.COM");
        user.NormalizedUserName = "USER4@EXAMPLE.COM";
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new UserStore(context);

        var byId = await sut.FindByIdAsync(user.Id.ToString(), TestContext.Current.CancellationToken);
        var byName = await sut.FindByNameAsync("USER4@EXAMPLE.COM", TestContext.Current.CancellationToken);
        var byEmail = await sut.FindByEmailAsync("USER4@EXAMPLE.COM", TestContext.Current.CancellationToken);

        Assert.NotNull(byId);
        Assert.NotNull(byName);
        Assert.NotNull(byEmail);
    }

    [Fact]
    public async Task GetSet_UserNameAndNormalizedUserName_Work()
    {
        await using var context = CreateContext();
        var sut = new UserStore(context);
        var user = NewUser("user5@example.com", "USER5@EXAMPLE.COM");

        await sut.SetUserNameAsync(user, "new@example.com", TestContext.Current.CancellationToken);
        await sut.SetNormalizedUserNameAsync(user, "NEW@EXAMPLE.COM", TestContext.Current.CancellationToken);

        Assert.Equal("new@example.com", await sut.GetUserNameAsync(user, TestContext.Current.CancellationToken));
        Assert.Equal("NEW@EXAMPLE.COM", await sut.GetNormalizedUserNameAsync(user, TestContext.Current.CancellationToken));
        Assert.Equal(user.Id.ToString(), await sut.GetUserIdAsync(user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSet_PasswordHashAndHasPassword_Work()
    {
        await using var context = CreateContext();
        var sut = new UserStore(context);
        var user = NewUser("user6@example.com", "USER6@EXAMPLE.COM");

        await sut.SetPasswordHashAsync(user, null, TestContext.Current.CancellationToken);
        Assert.False(await sut.HasPasswordAsync(user, TestContext.Current.CancellationToken));

        await sut.SetPasswordHashAsync(user, "hash", TestContext.Current.CancellationToken);
        Assert.True(await sut.HasPasswordAsync(user, TestContext.Current.CancellationToken));
        Assert.Equal("hash", await sut.GetPasswordHashAsync(user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSet_EmailAndConfirmation_Work()
    {
        await using var context = CreateContext();
        var sut = new UserStore(context);
        var user = NewUser("user7@example.com", "USER7@EXAMPLE.COM");

        await sut.SetEmailAsync(user, null, TestContext.Current.CancellationToken);
        await sut.SetNormalizedEmailAsync(user, null, TestContext.Current.CancellationToken);
        await sut.SetEmailConfirmedAsync(user, true, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, await sut.GetEmailAsync(user, TestContext.Current.CancellationToken));
        Assert.Equal(string.Empty, await sut.GetNormalizedEmailAsync(user, TestContext.Current.CancellationToken));
        Assert.True(await sut.GetEmailConfirmedAsync(user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSet_SecurityStampAndTwoFactor_Work()
    {
        await using var context = CreateContext();
        var sut = new UserStore(context);
        var user = NewUser("user8@example.com", "USER8@EXAMPLE.COM");

        await sut.SetSecurityStampAsync(user, "stamp", TestContext.Current.CancellationToken);
        await sut.SetTwoFactorEnabledAsync(user, true, TestContext.Current.CancellationToken);

        Assert.Equal("stamp", await sut.GetSecurityStampAsync(user, TestContext.Current.CancellationToken));
        Assert.True(await sut.GetTwoFactorEnabledAsync(user, TestContext.Current.CancellationToken));
    }

    private static UserDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase($"user-store-{Guid.NewGuid()}")
            .Options;

        return new UserDbContext(options);
    }

    private static UserEntity NewUser(string email, string normalizedEmail)
    {
        return new UserEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserName = email,
            Email = email,
            NormalizedEmail = normalizedEmail,
            NormalizedUserName = normalizedEmail,
            PasswordHash = "hash"
        };
    }
}
