using Bogdabot.Server.Infrastructure.Miscellaneous.Database;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Sqlite.Factories;

public class TraderDbContextFactory(
    ITraderDatabaseConfigFactory configFactory,
    IDbOptionsFactory dbOptionsFactory)
{
    public T Create<T>(
        NonEmptyGuid userGuid,
        string actorId)
        where T : DbContext
    {
        var config = configFactory.Create(userGuid, actorId);

        var optionsBuilder = new DbContextOptionsBuilder<T>();

        dbOptionsFactory.SetDatabaseOptions(optionsBuilder, config);

        return (T)Activator.CreateInstance(
            typeof(T),
            optionsBuilder.Options)!;
    }
}