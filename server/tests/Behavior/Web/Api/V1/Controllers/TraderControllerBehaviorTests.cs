using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.Users;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Web.Api.V1.Controllers;
using Bogdabot.Server.Web.Api.V1.Transformers;
using Bogdabot.Server.Web.Gateway;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Bogdabot.Server.Tests.Behavior.Web.Api.V1.Controllers;

public class TraderControllerBehaviorTests
{
    [Fact]
    public async Task CreateTrader_ReturnsCreatedWithLocationRouteValues()
    {
        var currentUser = CreateUser();
        var traderGuid = NonEmptyGuid.NewGuid();

        var controller = CreateController(currentUser, [CreateTrader(currentUser.Id, currentUser.Guid)], out var gateway);
        gateway.CreateTrader(Arg.Any<TraderConfiguration>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(traderGuid)));

        var result = await controller.CreateTrader(CreateValidFormData());

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(TraderController.GetOne), created.ActionName);
        Assert.Equal(traderGuid.Value, created.RouteValues!["traderGuid"]);
    }

    [Fact]
    public async Task StartStopDelete_Flow_ReturnsOk_ForExistingTrader()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(currentUser, [trader], out var gateway);

        gateway.StartActiveTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(trader.Guid)));
        gateway.StopOneTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(trader.Guid)));
        gateway.DeleteTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(trader.Guid)));

        var startResult = await controller.StartTrader(trader.Guid);
        var stopResult = await controller.StopOneTrader(trader.Guid);
        var deleteResult = await controller.DeleteTrader(trader.Guid);

        Assert.IsType<OkObjectResult>(startResult);
        Assert.IsType<OkObjectResult>(stopResult);
        Assert.IsType<OkObjectResult>(deleteResult);

        await gateway.Received(1).StartActiveTrader(Arg.Any<TraderIdentifier>());
        await gateway.Received(1).StopOneTrader(Arg.Any<TraderIdentifier>());
        await gateway.Received(1).DeleteTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsOk_ForStoppedTrader()
    {
        var currentUser = CreateUser();
        var stoppedTrader = CreateStoppedTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(currentUser, [], out var gateway, [stoppedTrader]);

        var result = await controller.DeleteTrader(stoppedTrader.Guid);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully deleted", Assert.IsType<string>(ok.Value));
        await gateway.DidNotReceive().DeleteTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task StartTrader_ReturnsNotFoundProblem_WhenTraderIsUnknown()
    {
        var controller = CreateController(CreateUser(), [], out _);

        var result = await controller.StartTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    private static TraderController CreateController(
        UserEntity? currentUser,
        IEnumerable<TraderEntity> traders,
        out IWebTraderGateway gateway,
        IEnumerable<StoppedTraderEntity>? stoppedTraders = null)
    {
        gateway = Substitute.For<IWebTraderGateway>();

        var currentUserService = Substitute.For<IFetchesCurrentUser>();
        currentUserService.GetCurrentUser().Returns(Task.FromResult(currentUser));

        var traderRepository = Substitute.For<ITraderRepository>();
        traderRepository.GetAllForUser(Arg.Any<ulong>())
            .Returns(call => Task.FromResult<IEnumerable<TraderEntity>>(
                traders.Where(t => t.UserId == call.Arg<ulong>()).ToList()));
        traderRepository.GetOneByUserIdAndGuid(Arg.Any<ulong>(), Arg.Any<NonEmptyGuid>())
            .Returns(call => Task.FromResult(
                traders.FirstOrDefault(t => t.UserId == call.ArgAt<ulong>(0) && t.Guid == call.ArgAt<NonEmptyGuid>(1))
            ));

        var stoppedTraderRepository = Substitute.For<IStoppedTraderRepository>();
        stoppedTraderRepository.GetOneByUserIdAndGuid(Arg.Any<ulong>(), Arg.Any<NonEmptyGuid>())
            .Returns(call => Task.FromResult(
                (stoppedTraders ?? []).FirstOrDefault(t => t.UserId == call.ArgAt<ulong>(0) && t.Guid == call.ArgAt<NonEmptyGuid>(1))
            ));
        stoppedTraderRepository.Delete(Arg.Any<StoppedTraderEntity>())
            .Returns(Task.CompletedTask);

        return new TraderController(
            gateway,
            new WebTransformer(),
            currentUserService,
            traderRepository,
            stoppedTraderRepository,
            Substitute.For<ILogger<TraderController>>()
        );
    }

    private static UserEntity CreateUser()
    {
        return new UserEntity
        {
            Id = 1,
            Guid = NonEmptyGuid.NewGuid(),
            UserName = "user@example.com",
            NormalizedUserName = "USER@EXAMPLE.COM",
            Email = "user@example.com",
            NormalizedEmail = "USER@EXAMPLE.COM"
        };
    }

    private static TraderEntity CreateTrader(ulong userId, NonEmptyGuid userGuid)
    {
        var now = DateTimeOffset.UtcNow;
        return new TraderEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserId = userId,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(10m),
            InitialBaseBalance = new NonNegativeDecimalNumber(10m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(5m),
            InitialTotalValue = new NonNegativeDecimalNumber(15m),
            FirstStartedAt = now,
            LastStartedAt = now,
            Status = TraderStatusEnum.Created
        };
    }

    private static StoppedTraderEntity CreateStoppedTrader(ulong userId, NonEmptyGuid userGuid)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoppedTraderEntity
        {
            Guid = NonEmptyGuid.NewGuid(),
            UserId = userId,
            UserGuid = userGuid,
            BrokerName = BrokerEnum.IBKR,
            StrategyName = TradingStrategyEnum.SpotGrid,
            BaseCurrency = new CurrencySymbol("BTC"),
            InitialPrice = new NonNegativeDecimalNumber(10m),
            InitialBaseBalance = new NonNegativeDecimalNumber(10m),
            FinalPrice = new NonNegativeDecimalNumber(9m),
            FinalBaseBalance = new NonNegativeDecimalNumber(9m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(5m),
            FinalQuoteBalance = new NonNegativeDecimalNumber(4m),
            InitialTotalValue = new NonNegativeDecimalNumber(15m),
            FinalTotalValue = new NonNegativeDecimalNumber(13m),
            UnrealizedResult = 0m,
            RealizedResult = 0m,
            TotalResult = 0m,
            UnrealizedProfitMargin = 0m,
            RealizedProfitMargin = 0m,
            TotalProfitMargin = 0m,
            StartedAt = now,
            StoppedAt = now
        };
    }

    private static CreateTraderFormData CreateValidFormData()
    {
        return new CreateTraderFormData
        {
            BrokerName = "IBKR",
            StrategyName = "SpotGrid",
            Base = "BTC",
            Quote = "EUR",
            InitialBaseInvestment = 25m,
            InitialQuoteInvestment = 10m
        };
    }
}
