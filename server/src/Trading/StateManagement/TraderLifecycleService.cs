using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Transformers;

namespace Bogdabot.Server.Trading.StateManagement;

public class TraderLifecycleService(
    Repositories.ITraderRepository traderRepository,
    Repositories.IStoppedTraderRepository stoppedTraderRepository,
    ITraderDatabaseManager traderDbManager,
    TraderEntityToTraderConfigurationTransformer traderConfigTransformer
) : ITraderLifecycleService
{
    private Repositories.ITraderRepository TraderRepository { get; } = traderRepository;

    private Repositories.IStoppedTraderRepository StoppedTraderRepository { get; } = stoppedTraderRepository;

    private ITraderDatabaseManager TraderDbManager { get; } = traderDbManager;

    private TraderEntityToTraderConfigurationTransformer TraderConfigTransformer { get; } = traderConfigTransformer;

    public async Task<NonEmptyGuid> CreateTrader(TraderConfiguration config, NonNegativeDecimalNumber initialPrice)
    {
        var trader = await TraderRepository.CreateAndSaveTrader(config, initialPrice);

        return trader.Guid;
    }

    public async Task MarkTraderRunning(NonEmptyGuid traderGuid)
    {
        var trader = await TraderRepository.GetOneByGuid(traderGuid)
    ?? throw new InvalidOperationException($"Trader with guid '{traderGuid}' could not be found.");

        trader.Status = TraderStatusEnum.Running;
        trader.LastStartedAt = DateTimeOffset.UtcNow;

        await TraderRepository.Save(trader);
    }

    public async Task MarkTraderFailed(NonEmptyGuid traderGuid)
    {
        var trader = await TraderRepository.GetOneByGuid(traderGuid)
    ?? throw new InvalidOperationException($"Trader with guid '{traderGuid}' could not be found.");

        trader.Status = TraderStatusEnum.Failed;

        await TraderRepository.Save(trader);
    }

    public async Task<TraderEntity> LoadTrader(NonEmptyGuid traderGuid)
    {
        return await TraderRepository.GetOneByGuid(traderGuid)
            ?? throw new InvalidOperationException($"Trader with guid '{traderGuid}' could not be found in the database.");
    }

    public async Task<TraderConfiguration> GetTraderConfiguration(NonEmptyGuid traderGuid)
    {
        var trader = await LoadTrader(traderGuid);

        return TraderConfigTransformer.transformActiveTrader(trader);
    }

    public async Task<TraderStatusEnum> GetCurrentStatus(NonEmptyGuid traderGuid)
    {
        var trader = await LoadTrader(traderGuid);

        return trader.Status;
    }

    public async Task PersistStoppedTrader(
        NonEmptyGuid traderGuid,
        NonNegativeDecimalNumber finalPrice,
        NonNegativeDecimalNumber finalBaseBalance,
        NonNegativeDecimalNumber finalQuoteBalance,
        decimal realizedResult
    )
    {
        var trader = await LoadTrader(traderGuid);

        await StoppedTraderRepository.SaveTraderAsStopped(
            trader: trader,
            finalPrice: finalPrice,
            finalBaseBalance: finalBaseBalance,
            finalQuoteBalance: finalQuoteBalance,
            realizedResult: realizedResult
        );
    }

    public async Task DeleteTraderByGuid(NonEmptyGuid traderGuid)
    {
        await TraderRepository.DeleteByGuid(traderGuid);
    }

    public void DeleteTraderDatabase(IDatabaseConfig databaseConfig)
    {
        TraderDbManager.DeleteDatabase(databaseConfig);
    }

    public async Task<IEnumerable<TraderIdentifier>> GetActiveTradersForUser(NonEmptyGuid userGuid)
    {
        var traders = await TraderRepository.GetActiveTradersForUser(userGuid);

        return traders
            .Select(trader => new TraderIdentifier
            {
                UserGuid = userGuid,
                TraderGuid = trader.Guid
            });
    }
}
