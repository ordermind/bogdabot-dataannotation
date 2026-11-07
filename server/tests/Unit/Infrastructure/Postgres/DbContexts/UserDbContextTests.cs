using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Postgres.DbContexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Postgres.DbContexts;

public class UserDbContextTests
{
    [Fact]
    public void Model_HasExpectedUserMappingsAndIgnoredIdentityTables()
    {
        using var context = CreateContext();
        var model = context.Model;

        var userEntityType = model.FindEntityType(typeof(UserEntity));
        Assert.NotNull(userEntityType);
        Assert.Equal("users", userEntityType!.GetTableName());

        Assert.NotNull(userEntityType.FindProperty(nameof(UserEntity.UserName)));
        Assert.NotNull(userEntityType.FindProperty(nameof(UserEntity.NormalizedEmail)));
        Assert.Null(userEntityType.FindProperty(nameof(UserEntity.PhoneNumber)));
        Assert.Null(userEntityType.FindProperty(nameof(UserEntity.SecurityStamp)));

        Assert.Null(model.FindEntityType(typeof(IdentityUserClaim<ulong>)));
        Assert.Null(model.FindEntityType(typeof(IdentityUserLogin<ulong>)));
        Assert.Null(model.FindEntityType(typeof(IdentityUserToken<ulong>)));
        Assert.Null(model.FindEntityType(typeof(IdentityRole<ulong>)));
        Assert.Null(model.FindEntityType(typeof(IdentityUserRole<ulong>)));
    }

    private static UserDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase($"user-dbctx-{Guid.NewGuid()}")
            .Options;

        return new UserDbContext(options);
    }
}
