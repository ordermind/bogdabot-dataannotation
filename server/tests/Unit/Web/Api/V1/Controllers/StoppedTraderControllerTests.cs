using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Users;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Entities;
using Bogdabot.Server.Trading.Enums;
using Bogdabot.Server.Web.Api.V1.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Bogdabot.Server.Tests.Unit.Web.Api.V1.Controllers;

public class StoppedTraderControllerTests
{
    [Fact]
    public async Task GetAll_ReturnsStoppedTradersForCurrentUser()
    {
        var currentUser = CreateUser();
        var stoppedTrader = CreateStoppedTrader(currentUser.Id, currentUser.Guid);
        var controller = CreateController(currentUser, [stoppedTrader], out var repository);

        var result = await controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var traders = Assert.IsAssignableFrom<IEnumerable<StoppedTraderEntity>>(ok.Value);
        Assert.Same(stoppedTrader, Assert.Single(traders));
        await repository.Received(1).GetAllForUser(currentUser.Id);
    }

    [Fact]
    public async Task GetAll_ReturnsServerProblem_WhenCurrentUserIsMissing()
    {
        var controller = CreateController(null, [], out var repository);

        var result = await controller.GetAll();

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await repository.DidNotReceive().GetAllForUser(Arg.Any<ulong>());
    }

    [Fact]
    public async Task GetOne_ReturnsStoppedTrader_WhenFoundForCurrentUser()
    {
        var currentUser = CreateUser();
        var stoppedTrader = CreateStoppedTrader(currentUser.Id, currentUser.Guid);
        var controller = CreateController(currentUser, [stoppedTrader], out var repository);

        var result = await controller.GetOne(stoppedTrader.Guid.Value);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(stoppedTrader, ok.Value);
        await repository.Received(1).GetOneByUserIdAndGuid(currentUser.Id, stoppedTrader.Guid);
    }

    [Fact]
    public async Task GetOne_ReturnsValidationProblem_WhenGuidIsEmpty()
    {
        var controller = CreateController(CreateUser(), [], out var repository);

        var result = await controller.GetOne(Guid.Empty);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
        await repository.DidNotReceive().GetOneByUserIdAndGuid(Arg.Any<ulong>(), Arg.Any<NonEmptyGuid>());
    }

    [Fact]
    public async Task GetOne_ReturnsServerProblem_WhenCurrentUserIsMissing()
    {
        var controller = CreateController(null, [], out var repository);

        var result = await controller.GetOne(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await repository.DidNotReceive().GetOneByUserIdAndGuid(Arg.Any<ulong>(), Arg.Any<NonEmptyGuid>());
    }

    [Fact]
    public async Task GetOne_ReturnsNotFound_WhenStoppedTraderDoesNotExistForCurrentUser()
    {
        var controller = CreateController(CreateUser(), [], out _);
        var traderGuid = Guid.NewGuid();

        var result = await controller.GetOne(traderGuid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal($"The stopped trader {traderGuid} could not be found.", Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    [Fact]
    public async Task DeleteTrader_ReturnsOkAndDeletesStoppedTrader_WhenFoundForCurrentUser()
    {
        var currentUser = CreateUser();
        var stoppedTrader = CreateStoppedTrader(currentUser.Id, currentUser.Guid);
        var controller = CreateController(currentUser, [stoppedTrader], out var repository);

        var result = await controller.DeleteTrader(stoppedTrader.Guid.Value);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully deleted", Assert.IsType<string>(ok.Value));
        await repository.Received(1).Delete(stoppedTrader);
    }

    [Fact]
    public async Task DeleteTrader_ReturnsValidationProblem_WhenGuidIsEmpty()
    {
        var controller = CreateController(CreateUser(), [], out var repository);

        var result = await controller.DeleteTrader(Guid.Empty);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
        await repository.DidNotReceive().Delete(Arg.Any<StoppedTraderEntity>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsServerProblem_WhenCurrentUserIsMissing()
    {
        var controller = CreateController(null, [], out var repository);

        var result = await controller.DeleteTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await repository.DidNotReceive().Delete(Arg.Any<StoppedTraderEntity>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsNotFound_WhenStoppedTraderDoesNotExistForCurrentUser()
    {
        var controller = CreateController(CreateUser(), [], out var repository);

        var result = await controller.DeleteTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        await repository.DidNotReceive().Delete(Arg.Any<StoppedTraderEntity>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsServerProblem_WhenRepositoryThrows()
    {
        var currentUser = CreateUser();
        var stoppedTrader = CreateStoppedTrader(currentUser.Id, currentUser.Guid);
        var controller = CreateController(currentUser, [stoppedTrader], out var repository);
        repository.Delete(stoppedTrader)
            .Returns(_ => Task.FromException(new InvalidOperationException("delete failed")));

        var result = await controller.DeleteTrader(stoppedTrader.Guid.Value);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        Assert.Equal("Trader deletion failed.", Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    private static StoppedTraderController CreateController(
        UserEntity? currentUser,
        IEnumerable<StoppedTraderEntity> stoppedTraders,
        out IStoppedTraderRepository repository)
    {
        var currentUserService = Substitute.For<IFetchesCurrentUser>();
        currentUserService.GetCurrentUser().Returns(Task.FromResult(currentUser));

        repository = Substitute.For<IStoppedTraderRepository>();
        repository.GetAllForUser(Arg.Any<ulong>())
            .Returns(call => Task.FromResult<IEnumerable<StoppedTraderEntity>>(
                stoppedTraders.Where(trader => trader.UserId == call.Arg<ulong>()).ToList()));
        repository.GetOneByUserIdAndGuid(Arg.Any<ulong>(), Arg.Any<NonEmptyGuid>())
            .Returns(call => Task.FromResult(stoppedTraders.FirstOrDefault(trader =>
                trader.UserId == call.ArgAt<ulong>(0) && trader.Guid == call.ArgAt<NonEmptyGuid>(1))));
        repository.Delete(Arg.Any<StoppedTraderEntity>()).Returns(Task.CompletedTask);

        return new StoppedTraderController(
            currentUserService,
            repository,
            Substitute.For<ILogger<StoppedTraderController>>());
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
}