using Microsoft.AspNetCore.Mvc;
using Bogdabot.Server.Web.Api.V1.Transformers;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Users;
using Bogdabot.Server.Web.Gateway;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Shared.Errors;

namespace Bogdabot.Server.Web.Api.V1.Controllers;

[ApiController]
[Route("api/v1/traders")]
public class TraderController(
    IWebTraderGateway traderGateway,
    WebTransformer transformer,
    IFetchesCurrentUser currentUserService,
    ITraderRepository traderRepository,
    IStoppedTraderRepository stoppedTraderRepository,
    ILogger<TraderController> logger
    ) : ControllerBase
{
    private IWebTraderGateway TraderGateway { get; } = traderGateway;

    private WebTransformer Transformer { get; } = transformer;

    private IFetchesCurrentUser CurrentUserService { get; } = currentUserService;

    private Shared.Trading.ITraderRepository TraderRepository { get; } = traderRepository;

    private IStoppedTraderRepository StoppedTraderRepository { get; } = stoppedTraderRepository;

    private ILogger<TraderController> Logger { get; } = logger;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var currentUser = await CurrentUserService.GetCurrentUser();
        if (currentUser is null)
        {
            return Problem(
                title: "The currently logged in user could not be fetched.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        var entities = await TraderRepository.GetAllForUser(currentUser.Id);

        return Ok(entities);
    }

    [HttpGet("{traderGuid:guid}")]
    public async Task<IActionResult> GetOne(Guid traderGuid)
    {
        if (traderGuid == Guid.Empty)
        {
            ModelState.AddModelError(nameof(traderGuid), "The traderGuid field must be a non-empty GUID.");
            return ValidationProblem(ModelState);
        }

        var validatedTraderGuid = NonEmptyGuid.FromValue(traderGuid);

        var currentUser = await CurrentUserService.GetCurrentUser();
        if (currentUser is null)
        {
            return Problem(
                title: "The currently logged in user could not be fetched.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        var trader = await TraderRepository.GetOneByUserIdAndGuid(currentUser.Id, validatedTraderGuid);

        if (trader is null)
        {
            return Problem(
                title: $"The trader {traderGuid} could not be found.",
                statusCode: StatusCodes.Status404NotFound
            );
        }

        return Ok(trader);
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateTrader(
        [FromBody] CreateTraderFormData traderData
    )
    {
        var currentUser = await CurrentUserService.GetCurrentUser();
        if (currentUser is null)
        {
            return Problem(
                title: "The currently logged in user could not be fetched.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        var traderConfig = Transformer.TransformCreateTraderFormDataToTraderConfiguration(traderData, currentUser.Id, currentUser.Guid);

        try
        {
            var result = await this.TraderGateway.CreateTrader(traderConfig);

            switch (result)
            {
                case Result<NonEmptyGuid>.Ok(var traderGuid):
                    var traderEntity = await TraderRepository.GetOneByUserIdAndGuid(currentUser.Id, traderGuid);

                    return CreatedAtAction(nameof(GetOne), new { traderGuid = traderGuid.Value }, traderEntity);

                case Result<NonEmptyGuid>.Error(var errorValue):
                    if (errorValue is AbstractUserError)
                    {
                        return Problem(
                            title: errorValue,
                            statusCode: StatusCodes.Status400BadRequest
                        );
                    }

                    Logger.LogError("Failed to create trader for user {UserId}: {message}", currentUser.Id, errorValue);

                    return Problem(
                        title: "The trader could not be created.",
                        statusCode: StatusCodes.Status500InternalServerError
                    );
                default:
                    throw new Exception("Unknown result type");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to dispatch create trader request for user {UserId}", currentUser.Id);
            return Problem(
                title: "Failed to dispatch create trader request.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    [HttpPost("{traderGuid:guid}/start")]
    public async Task<IActionResult> StartTrader(Guid traderGuid)
    {
        if (traderGuid == Guid.Empty)
        {
            ModelState.AddModelError(nameof(traderGuid), "The traderGuid field must be a non-empty GUID.");
            return ValidationProblem(ModelState);
        }

        var validatedTraderGuid = NonEmptyGuid.FromValue(traderGuid);

        var currentUser = await CurrentUserService.GetCurrentUser();
        if (currentUser is null)
        {
            return Problem(
                title: "The currently logged in user could not be fetched.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        var stoppedTrader = await StoppedTraderRepository.GetOneByUserIdAndGuid(currentUser.Id, validatedTraderGuid);
        if (stoppedTrader != null)
        {
            return Problem(
                title: $"The stopped trader {validatedTraderGuid} could not be restarted. Please create a new trader instead.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var trader = await TraderRepository.GetOneByUserIdAndGuid(currentUser.Id, validatedTraderGuid);
        if (trader is null)
        {
            return Problem(
                title: $"The trader {traderGuid} could not be found.",
                statusCode: StatusCodes.Status404NotFound
            );
        }

        try
        {
            var result = await this.TraderGateway.StartActiveTrader(
               new TraderIdentifier
               {
                   TraderGuid = validatedTraderGuid,
                   UserGuid = currentUser.Guid
               }
            );

            switch (result)
            {
                case Result<NonEmptyGuid>.Ok:
                    return Ok($"The trader {traderGuid} has been successfully started.");

                case Result<NonEmptyGuid>.Error(var errorValue):
                    if (errorValue is IllegalStatusTransitionError)
                    {
                        ModelState.AddModelError(nameof(traderGuid), errorValue);

                        return ValidationProblem(ModelState);
                    }

                    if (errorValue is AbstractUserError)
                    {
                        return Problem(
                            title: errorValue,
                            statusCode: StatusCodes.Status400BadRequest
                        );
                    }

                    Logger.LogError("Failed to start trader {TraderGuid}: {message}", traderGuid, errorValue);

                    return Problem(
                        title: "The trader could not be started.",
                        statusCode: StatusCodes.Status500InternalServerError
                    );
                default:
                    throw new Exception("Unknown result type");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to start trader {TraderGuid}", validatedTraderGuid);

            return Problem(
                title: "Trader start failed.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Stops all running traders for this user.
    /// </summary>
    [HttpPost("stop")]
    public async Task<IActionResult> StopAllTraders()
    {
        var currentUser = await CurrentUserService.GetCurrentUser();
        if (currentUser is null)
        {
            return Problem(
                title: "The currently logged in user could not be fetched.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        try
        {
            var result = await this.TraderGateway.StopAllTradersForUser(currentUser.Guid);

            switch (result)
            {
                case Result<NonEmptyGuid>.Ok:
                    return Ok($"All the traders for user {currentUser.Guid} have been successfully stopped.");

                case Result<NonEmptyGuid>.Error(var errorValue):
                    if (errorValue is AbstractUserError)
                    {
                        return Problem(
                            title: errorValue,
                            statusCode: StatusCodes.Status400BadRequest
                        );
                    }

                    Logger.LogError("Failed to stop the traders for user {UserGuid}: {message}", currentUser.Guid, errorValue);

                    return Problem(
                        title: "The traders for user {currentUser.Guid} could not be stopped.",
                        statusCode: StatusCodes.Status500InternalServerError
                    );
                default:
                    throw new Exception("Unknown result type");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to stop the traders for user  {UserGuid}", currentUser.Guid);

            return Problem(
                title: "The traders for user {currentUser.Guid} could not be stopped.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    [HttpPost("{traderGuid:guid}/stop")]
    public async Task<IActionResult> StopOneTrader(Guid traderGuid)
    {
        if (traderGuid == Guid.Empty)
        {
            ModelState.AddModelError(nameof(traderGuid), "The traderGuid field must be a non-empty GUID.");
            return ValidationProblem(ModelState);
        }

        var validatedTraderGuid = NonEmptyGuid.FromValue(traderGuid);

        var currentUser = await CurrentUserService.GetCurrentUser();
        if (currentUser is null)
        {
            return Problem(
                title: "The currently logged in user could not be fetched.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        var trader = await TraderRepository.GetOneByUserIdAndGuid(currentUser.Id, validatedTraderGuid);
        if (trader is null)
        {
            return Problem(
                title: $"The trader {traderGuid} could not be found.",
                statusCode: StatusCodes.Status404NotFound
            );
        }

        try
        {
            var result = await this.TraderGateway.StopOneTrader(
               new TraderIdentifier
               {
                   TraderGuid = validatedTraderGuid,
                   UserGuid = currentUser.Guid
               }
            );

            switch (result)
            {
                case Result<NonEmptyGuid>.Ok:
                    return Ok($"The trader {validatedTraderGuid} has been successfully stopped.");

                case Result<NonEmptyGuid>.Error(var errorValue):
                    if (errorValue is IllegalStatusTransitionError)
                    {
                        ModelState.AddModelError(nameof(traderGuid), errorValue);

                        return ValidationProblem(ModelState);
                    }

                    if (errorValue is AbstractUserError)
                    {
                        return Problem(
                            title: errorValue,
                            statusCode: StatusCodes.Status400BadRequest
                        );
                    }

                    Logger.LogError("Failed to stop trader {TraderGuid}: {message}", validatedTraderGuid, errorValue);

                    return Problem(
                        title: "The trader could not be stopped.",
                        statusCode: StatusCodes.Status500InternalServerError
                    );
                default:
                    throw new Exception("Unknown result type");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to stop trader {TraderGuid}", validatedTraderGuid);
            return Problem(
                title: "Trader stop failed.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    [HttpPost("{traderGuid:guid}/delete")]
    public async Task<IActionResult> DeleteTrader(Guid traderGuid)
    {
        if (traderGuid == Guid.Empty)
        {
            ModelState.AddModelError(nameof(traderGuid), "The traderGuid field must be a non-empty GUID.");
            return ValidationProblem(ModelState);
        }

        var validatedTraderGuid = NonEmptyGuid.FromValue(traderGuid);

        var currentUser = await CurrentUserService.GetCurrentUser();
        if (currentUser is null)
        {
            return Problem(
                title: "The currently logged in user could not be fetched.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }

        var stoppedTrader = await StoppedTraderRepository.GetOneByUserIdAndGuid(currentUser.Id, validatedTraderGuid);
        if (stoppedTrader != null)
        {
            try
            {
                await StoppedTraderRepository.Delete(stoppedTrader);

                return Ok($"The (stopped) trader {validatedTraderGuid} has been successfully deleted.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to delete (stopped) trader {TraderGuid}", validatedTraderGuid);
                return Problem(
                    title: "Trader deletion failed.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        var activeTrader = await TraderRepository.GetOneByUserIdAndGuid(currentUser.Id, validatedTraderGuid);
        if (activeTrader is null)
        {
            return Problem(
                title: $"The trader {traderGuid} could not be found.",
                statusCode: StatusCodes.Status404NotFound
            );
        }

        try
        {
            var result = await this.TraderGateway.DeleteTrader(
               new TraderIdentifier
               {
                   TraderGuid = validatedTraderGuid,
                   UserGuid = currentUser.Guid
               }
            );

            switch (result)
            {
                case Result<NonEmptyGuid>.Ok:
                    return Ok($"The trader {validatedTraderGuid} has been successfully deleted.");

                case Result<NonEmptyGuid>.Error(var errorValue):
                    if (errorValue is AbstractUserError)
                    {
                        return Problem(
                            title: errorValue,
                            statusCode: StatusCodes.Status400BadRequest
                        );
                    }

                    Logger.LogError("Failed to delete trader {TraderGuid}: {message}", validatedTraderGuid, errorValue);

                    return Problem(
                        title: "The trader could not be deleted.",
                        statusCode: StatusCodes.Status500InternalServerError
                    );
                default:
                    throw new Exception("Unknown result type");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete trader {TraderGuid}", validatedTraderGuid);
            return Problem(
                title: "Trader deletion failed.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
