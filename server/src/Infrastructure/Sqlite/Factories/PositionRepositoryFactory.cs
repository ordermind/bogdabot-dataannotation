using Bogdabot.Server.Infrastructure.Sqlite.Repositories;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Repositories;

namespace Bogdabot.Server.Infrastructure.Sqlite.Factories;

public class PositionRepositoryFactory(
    TraderDbContextFactory contextFactory)
    : IPositionRepositoryFactory
{
    public IPositionRepository Create(
        NonEmptyGuid userGuid,
        string actorId)
    {
        return new PositionRepository(
            contextFactory,
            userGuid,
            actorId);
    }
}