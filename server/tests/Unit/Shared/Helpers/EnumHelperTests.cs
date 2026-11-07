using Bogdabot.Server.Shared.Enums;
using Bogdabot.Server.Shared.Helpers;

namespace Bogdabot.Server.Tests.Unit.Shared.Helpers;

public class EnumHelperTests
{
    [Fact]
    public void GetValues_Returns_AllEnumValues()
    {
        var values = EnumHelper.GetValues<DatabaseStorageTypeEnum>().ToArray();

        Assert.Contains(DatabaseStorageTypeEnum.PostgreSQL, values);
        Assert.Contains(DatabaseStorageTypeEnum.SQLite, values);
        Assert.Equal(2, values.Length);
    }
}
