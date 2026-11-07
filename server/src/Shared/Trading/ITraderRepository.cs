using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;

namespace Bogdabot.Server.Shared.Trading;

public interface ITraderRepository
{
    Task<IEnumerable<TraderEntity>> GetAllForUser(ulong userId);

    Task<TraderEntity?> GetOneByUserIdAndGuid(ulong userId, NonEmptyGuid traderGuid);
}