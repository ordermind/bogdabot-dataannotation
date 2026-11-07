using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Config.Database;

public interface ITraderDatabaseConfigFactory
{
    public IDatabaseConfig Create(NonEmptyGuid userGuid, string actorId);
}
