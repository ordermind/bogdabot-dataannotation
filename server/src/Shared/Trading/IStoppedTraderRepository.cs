using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;

namespace Bogdabot.Server.Shared.Trading;

public interface IStoppedTraderRepository
{
    Task<IEnumerable<StoppedTraderEntity>> GetAllForUser(ulong userId);
    Task<StoppedTraderEntity?> GetOneByUserIdAndGuid(ulong userId, NonEmptyGuid traderGuid);
    Task Delete(StoppedTraderEntity stoppedTrader);
}