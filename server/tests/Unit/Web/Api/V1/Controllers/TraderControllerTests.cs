using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Shared.Errors;
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

namespace Bogdabot.Server.Tests.Unit.Web.Api.V1.Controllers;

public class TraderControllerTests
{
    [Fact]
    public async Task GetOne_ReturnsOk_WhenTraderExistsForCurrentUser()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out _
        );

        var result = await controller.GetOne(trader.Guid);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<TraderEntity>(ok.Value);
        Assert.Equal(trader.Guid, payload.Guid);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyCurrentUsersTraders()
    {
        var currentUser = CreateUser();
        var ownedTrader = CreateTrader(currentUser.Id, currentUser.Guid);
        var otherTrader = CreateTrader(2, NonEmptyGuid.NewGuid());

        var controller = CreateController(
            currentUser,
            [ownedTrader, otherTrader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out _
        );

        var result = await controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var traders = Assert.IsAssignableFrom<IEnumerable<TraderEntity>>(ok.Value).ToList();

        Assert.Single(traders);
        Assert.Equal(ownedTrader.Guid, traders[0].Guid);
    }

    [Fact]
    public async Task CreateTrader_ReturnsCreatedAtAction_WhenGatewayCreatesTrader()
    {
        var currentUser = CreateUser();
        var createdTraderGuid = NonEmptyGuid.NewGuid();
        var createdTrader = CreateTrader(currentUser.Id, currentUser.Guid);
        createdTrader.Guid = createdTraderGuid;

        var controller = CreateController(
            currentUser,
            [createdTrader],
            new Result<NonEmptyGuid>.Ok(createdTraderGuid),
            out var gateway
        );

        var result = await controller.CreateTrader(CreateValidFormData());

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(TraderController.GetOne), created.ActionName);
        Assert.Equal(createdTraderGuid.Value, ((TraderEntity)created.Value!).Guid.Value);

        await gateway.Received(1).CreateTrader(Arg.Any<TraderConfiguration>());
    }

    [Fact]
    public async Task CreateTrader_ReturnsProblem_WhenCurrentUserMissing()
    {
        var controller = CreateController(
            currentUser: null,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.CreateTrader(CreateValidFormData());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await gateway.DidNotReceive().CreateTrader(Arg.Any<TraderConfiguration>());
    }

    [Fact]
    public async Task CreateTrader_ReturnsBadRequestProblem_WhenGatewayReturnsUserError()
    {
        var currentUser = CreateUser();
        var userError = new TraderStartUserError("invalid request");

        var controller = CreateController(
            currentUser,
            [],
            new Result<NonEmptyGuid>.Error(userError),
            out _
        );

        var result = await controller.CreateTrader(CreateValidFormData());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateTrader_ReturnsServerProblem_WhenGatewayReturnsInternalError()
    {
        var currentUser = CreateUser();

        var controller = CreateController(
            currentUser,
            [],
            new Result<NonEmptyGuid>.Error(new FailedDatabaseWriteError("db down")),
            out _
        );

        var result = await controller.CreateTrader(CreateValidFormData());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task CreateTrader_ReturnsServerProblem_WhenGatewayThrows()
    {
        var currentUser = CreateUser();
        var controller = CreateController(
            currentUser,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.CreateTrader(Arg.Any<TraderConfiguration>())
            .Returns(_ => Task.FromException<Result<NonEmptyGuid>>(new InvalidOperationException("boom")));

        var result = await controller.CreateTrader(CreateValidFormData());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task StartTrader_ReturnsNotFoundProblem_WhenTraderDoesNotExistForUser()
    {
        var currentUser = CreateUser();
        var controller = CreateController(
            currentUser,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out _
        );

        var result = await controller.StartTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    [Fact]
    public async Task StartTrader_ReturnsValidationProblem_WhenGuidIsEmpty()
    {
        var controller = CreateController(
            CreateUser(),
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.StartTrader(Guid.Empty);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
        await gateway.DidNotReceive().StartActiveTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task StartTrader_ReturnsProblem_WhenCurrentUserMissing()
    {
        var controller = CreateController(
            currentUser: null,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.StartTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await gateway.DidNotReceive().StartActiveTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task StartTrader_ReturnsValidationProblem_WhenGatewayReturnsIllegalStatusTransition()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StartActiveTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new IllegalStatusTransitionError("bad transition"))));

        var result = await controller.StartTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
    }

    [Fact]
    public async Task StartTrader_ReturnsBadRequestProblem_WhenGatewayReturnsUserError()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StartActiveTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new TraderStartUserError("user error"))));

        var result = await controller.StartTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task StartTrader_ReturnsServerProblem_WhenGatewayReturnsInternalError()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StartActiveTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new FailedDatabaseReadError("db down"))));

        var result = await controller.StartTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task StartTrader_ReturnsServerProblem_WhenGatewayThrows()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StartActiveTrader(Arg.Any<TraderIdentifier>())
            .Returns(_ => Task.FromException<Result<NonEmptyGuid>>(new InvalidOperationException("boom")));

        var result = await controller.StartTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task StopTrader_ReturnsOk_WhenGatewayStopsTrader()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StopOneTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(trader.Guid)));

        var result = await controller.StopOneTrader(trader.Guid);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully stopped", Assert.IsType<string>(ok.Value));
        await gateway.Received(1).StopOneTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task StopTrader_ReturnsValidationProblem_WhenGuidIsEmpty()
    {
        var controller = CreateController(
            CreateUser(),
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.StopOneTrader(Guid.Empty);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
        await gateway.DidNotReceive().StopOneTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task StopTrader_ReturnsProblem_WhenCurrentUserMissing()
    {
        var controller = CreateController(
            currentUser: null,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.StopOneTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await gateway.DidNotReceive().StopOneTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task StopTrader_ReturnsNotFoundProblem_WhenTraderDoesNotExistForUser()
    {
        var controller = CreateController(
            CreateUser(),
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.StopOneTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        await gateway.DidNotReceive().StopOneTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task StopTrader_ReturnsServerProblem_WhenGatewayReturnsInternalError()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StopOneTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new FailedDatabaseReadError("db down"))));

        var result = await controller.StopOneTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task StopTrader_ReturnsValidationProblem_WhenGatewayReturnsIllegalStatusTransition()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StopOneTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new IllegalStatusTransitionError("bad transition"))));

        var result = await controller.StopOneTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
    }

    [Fact]
    public async Task StopTrader_ReturnsBadRequestProblem_WhenGatewayReturnsUserError()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StopOneTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new TraderStopUserError("bad request"))));

        var result = await controller.StopOneTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task StopTrader_ReturnsServerProblem_WhenGatewayThrows()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.StopOneTrader(Arg.Any<TraderIdentifier>())
            .Returns(_ => Task.FromException<Result<NonEmptyGuid>>(new InvalidOperationException("boom")));

        var result = await controller.StopOneTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task DeleteTrader_ReturnsServerProblem_WhenGatewayReturnsInternalError()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.DeleteTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new FailedDatabaseReadError("db down"))));

        var result = await controller.DeleteTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task DeleteTrader_ReturnsOk_WhenGatewayDeletesTrader()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.DeleteTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(trader.Guid)));

        var result = await controller.DeleteTrader(trader.Guid);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully deleted", Assert.IsType<string>(ok.Value));
    }

    [Fact]
    public async Task DeleteTrader_ReturnsOk_WhenStoppedTraderExists()
    {
        var currentUser = CreateUser();
        var stoppedTrader = CreateStoppedTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway,
            [stoppedTrader]
        );

        var result = await controller.DeleteTrader(stoppedTrader.Guid);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully deleted", Assert.IsType<string>(ok.Value));
        await gateway.DidNotReceive().DeleteTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsValidationProblem_WhenGuidIsEmpty()
    {
        var controller = CreateController(
            CreateUser(),
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.DeleteTrader(Guid.Empty);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
        await gateway.DidNotReceive().DeleteTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsProblem_WhenCurrentUserMissing()
    {
        var controller = CreateController(
            currentUser: null,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.DeleteTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await gateway.DidNotReceive().DeleteTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsNotFoundProblem_WhenTraderDoesNotExistForUser()
    {
        var controller = CreateController(
            CreateUser(),
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        var result = await controller.DeleteTrader(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        await gateway.DidNotReceive().DeleteTrader(Arg.Any<TraderIdentifier>());
    }

    [Fact]
    public async Task DeleteTrader_ReturnsBadRequestProblem_WhenGatewayReturnsUserError()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.DeleteTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Error(new TraderDeleteUserError("bad request"))));

        var result = await controller.DeleteTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task DeleteTrader_ReturnsServerProblem_WhenGatewayThrows()
    {
        var currentUser = CreateUser();
        var trader = CreateTrader(currentUser.Id, currentUser.Guid);

        var controller = CreateController(
            currentUser,
            [trader],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out var gateway
        );

        gateway.DeleteTrader(Arg.Any<TraderIdentifier>())
            .Returns(_ => Task.FromException<Result<NonEmptyGuid>>(new InvalidOperationException("boom")));

        var result = await controller.DeleteTrader(trader.Guid);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task GetOne_ReturnsValidationProblem_WhenGuidIsEmpty()
    {
        var controller = CreateController(
            CreateUser(),
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out _
        );

        var result = await controller.GetOne(Guid.Empty);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(problem.Value);
    }

    [Fact]
    public async Task GetOne_ReturnsProblem_WhenCurrentUserMissing()
    {
        var controller = CreateController(
            currentUser: null,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out _
        );

        var result = await controller.GetOne(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    [Fact]
    public async Task GetOne_ReturnsNotFoundProblem_WhenTraderDoesNotExistForCurrentUser()
    {
        var controller = CreateController(
            CreateUser(),
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out _
        );

        var result = await controller.GetOne(Guid.NewGuid());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsProblem_WhenCurrentUserMissing()
    {
        var controller = CreateController(
            currentUser: null,
            [],
            new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid()),
            out _
        );

        var result = await controller.GetAll();

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
    }

    private static TraderController CreateController(
        UserEntity? currentUser,
        IEnumerable<TraderEntity> traders,
        Result<NonEmptyGuid> createResult,
        out IWebTraderGateway gateway,
        IEnumerable<StoppedTraderEntity>? stoppedTraders = null
    )
    {
        gateway = Substitute.For<IWebTraderGateway>();
        gateway.CreateTrader(Arg.Any<TraderConfiguration>()).Returns(Task.FromResult(createResult));
        gateway.StartActiveTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid())));
        gateway.StopOneTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid())));
        gateway.DeleteTrader(Arg.Any<TraderIdentifier>())
            .Returns(Task.FromResult<Result<NonEmptyGuid>>(new Result<NonEmptyGuid>.Ok(NonEmptyGuid.NewGuid())));

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
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(10m),
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
            InitialPrice = new NonNegativeDecimalNumber(100m),
            InitialBaseBalance = new NonNegativeDecimalNumber(10m),
            FinalPrice = new NonNegativeDecimalNumber(90m),
            FinalBaseBalance = new NonNegativeDecimalNumber(8m),
            QuoteCurrency = new CurrencySymbol("EUR"),
            InitialQuoteBalance = new NonNegativeDecimalNumber(5m),
            FinalQuoteBalance = new NonNegativeDecimalNumber(4m),
            InitialTotalValue = new NonNegativeDecimalNumber(15m),
            FinalTotalValue = new NonNegativeDecimalNumber(12m),
            UnrealizedResult = 1m,
            RealizedResult = 0m,
            TotalResult = 1m,
            UnrealizedProfitMargin = 0.1m,
            RealizedProfitMargin = 0m,
            TotalProfitMargin = 0.1m,
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
