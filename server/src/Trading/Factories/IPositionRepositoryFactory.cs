using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Repositories;

namespace Bogdabot.Server.Trading.Factories;

public interface IPositionRepositoryFactory
{
    IPositionRepository Create(
        NonEmptyGuid userGuid,
        string actorId
    );
}