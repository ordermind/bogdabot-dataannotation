using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;
using NSubstitute;

namespace Bogdabot.Server.Tests.Unit.Trading.StateManagement;

public class TraderLifecycleServiceTests
{
    [Fact]
    public async Task CreateTrader_PersistsTrader_AndReturnsGuid()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var config = CreateConfig();
        var initialPrice = new NonNegativeDecimalNumber(123.45m);
        var createdGuid = NonEmptyGuid.NewGuid();
        var entity = CreateTraderEntity(createdGuid, TraderStatusEnum.Created, config.UserGuid);

        repository.CreateAndSaveTrader(config, initialPrice)
            .Returns(Task.FromResult(entity));

        var result = await sut.CreateTrader(config, initialPrice);

        Assert.Equal(createdGuid, result);
        await repository.Received(1).CreateAndSaveTrader(config, initialPrice);
    }

    [Fact]
    public async Task MarkTraderRunning_UpdatesStatusAndStartedAt_AndSaves()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var guid = NonEmptyGuid.NewGuid();
        var entity = CreateTraderEntity(guid, TraderStatusEnum.Created);
        repository.GetOneByGuid(guid).Returns(entity);

        await sut.MarkTraderRunning(guid);

        Assert.Equal(TraderStatusEnum.Running, entity.Status);
        Assert.NotEqual(default, entity.LastStartedAt);
        await repository.Received(1).Save(entity);
    }

    [Fact]
    public async Task MarkTraderFailed_UpdatesStatus_AndSaves()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var guid = NonEmptyGuid.NewGuid();
        var entity = CreateTraderEntity(guid, TraderStatusEnum.Running);
        repository.GetOneByGuid(guid).Returns(entity);

        await sut.MarkTraderFailed(guid);

        Assert.Equal(TraderStatusEnum.Failed, entity.Status);
        await repository.Received(1).Save(entity);
    }

    [Fact]
    public async Task GetActiveTradersForUser_MapsToTraderIdentifiers()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var userGuid = NonEmptyGuid.NewGuid();
        var traderGuid = NonEmptyGuid.NewGuid();

        repository.GetActiveTradersForUser(userGuid)
            .Returns(Task.FromResult<IEnumerable<TraderEntity>>([
                CreateTraderEntity(traderGuid, TraderStatusEnum.Running, userGuid)
            ]));

        var result = (await sut.GetActiveTradersForUser(userGuid)).ToList();

        Assert.Single(result);
        Assert.Equal(userGuid, result[0].UserGuid);
        Assert.Equal(traderGuid, result[0].TraderGuid);
    }

    [Fact]
    public async Task GetTraderConfiguration_ReturnsMappedConfiguration()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var guid = NonEmptyGuid.NewGuid();
        var entity = CreateTraderEntity(guid, TraderStatusEnum.Created);
        repository.GetOneByGuid(guid).Returns(entity);

        var result = await sut.GetTraderConfiguration(guid);

        Assert.Equal(entity.UserId, result.UserId);
        Assert.Equal(entity.UserGuid, result.UserGuid);
        Assert.Equal(entity.BrokerName, result.BrokerName);
        Assert.Equal(entity.StrategyName, result.StrategyName);
        Assert.Equal(entity.BaseCurrency, result.BaseCurrency);
        Assert.Equal(entity.QuoteCurrency, result.QuoteCurrency);
        Assert.Equal(entity.InitialBaseBalance, result.InitialBaseInvestment);
        Assert.Equal(entity.InitialQuoteBalance, result.InitialQuoteInvestment);
    }

    [Fact]
    public async Task GetTraderConfiguration_WhenTraderMissing_ThrowsInvalidOperationException()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var guid = NonEmptyGuid.NewGuid();
        repository.GetOneByGuid(guid).Returns(Task.FromResult<TraderEntity?>(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetTraderConfiguration(guid));
    }

    [Fact]
    public async Task GetCurrentStatus_ReturnsTraderStatus()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var guid = NonEmptyGuid.NewGuid();
        var entity = CreateTraderEntity(guid, TraderStatusEnum.Running);
        repository.GetOneByGuid(guid).Returns(entity);

        var result = await sut.GetCurrentStatus(guid);

        Assert.Equal(TraderStatusEnum.Running, result);
    }

    [Fact]
    public async Task PersistStoppedTrader_DelegatesToStoppedRepository()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var guid = NonEmptyGuid.NewGuid();
        var trader = CreateTraderEntity(guid, TraderStatusEnum.Running);
        repository.GetOneByGuid(guid).Returns(trader);

        var finalPrice = new NonNegativeDecimalNumber(120m);
        var finalBaseBalance = new NonNegativeDecimalNumber(10m);
        var finalQuoteBalance = new NonNegativeDecimalNumber(20m);

        await sut.PersistStoppedTrader(guid, finalPrice, finalBaseBalance, finalQuoteBalance, 5m);

        await stoppedRepository.Received(1).SaveTraderAsStopped(trader, finalPrice, finalBaseBalance, finalQuoteBalance, 5m);
    }

    [Fact]
    public async Task DeleteTraderByGuid_DelegatesToRepository()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var guid = NonEmptyGuid.NewGuid();

        await sut.DeleteTraderByGuid(guid);

        await repository.Received(1).DeleteByGuid(guid);
    }

    [Fact]
    public void DeleteTraderDatabase_DelegatesToDatabaseManager()
    {
        var repository = Substitute.For<Bogdabot.Server.Trading.Repositories.ITraderRepository>();
        var stoppedRepository = Substitute.For<Bogdabot.Server.Trading.Repositories.IStoppedTraderRepository>();
        var dbManager = Substitute.For<ITraderDatabaseManager>();
        var transformer = new TraderEntityToTraderConfigurationTransformer();
        var sut = new TraderLifecycleService(repository, stoppedRepository, dbManager, transformer);

        var databaseConfig = Substitute.For<IDatabaseConfig>();

        sut.DeleteTraderDatabase(databaseConfig);

        dbManager.Received(1).DeleteDatabase(databaseConfig);
    }

    private static TraderConfiguration CreateConfig()
    {
        return new TraderConfiguration
        {
            UserId = 1,
            UserGuid = NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialBaseInvestment = new NonNegativeDecimalNumber(100m),
            InitialQuoteInvestment = new NonNegativeDecimalNumber(50m)
        };
    }

    private static TraderEntity CreateTraderEntity(NonEmptyGuid guid, TraderStatusEnum status, NonEmptyGuid? userGuid = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new TraderEntity
        {
            Guid = guid,
            UserId = 1,
            UserGuid = userGuid ?? NonEmptyGuid.NewGuid(),
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(100m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(50m),
            InitialTotalValue = new NonNegativeDecimalNumber(150m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = status
        };
    }
}
