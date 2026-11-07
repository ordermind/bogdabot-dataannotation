namespace Bogdabot.Server.Web.Api.V1.Controllers;

using Microsoft.AspNetCore.Mvc;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Users;
using Bogdabot.Server.Shared.ValueObjects;

[ApiController]
[Route("api/v1/stopped-traders")]
public class StoppedTraderController(
    IFetchesCurrentUser currentUserService,
    IStoppedTraderRepository stoppedTraderRepository,
    ILogger<StoppedTraderController> logger
) : ControllerBase
{
    private IFetchesCurrentUser CurrentUserService { get; } = currentUserService;
    private IStoppedTraderRepository StoppedTraderRepository { get; } = stoppedTraderRepository;
    private ILogger<StoppedTraderController> Logger { get; } = logger;

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

        var entities = await StoppedTraderRepository.GetAllForUser(currentUser.Id);

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

        var stoppedTrader = await StoppedTraderRepository.GetOneByUserIdAndGuid(currentUser.Id, validatedTraderGuid);

        if (stoppedTrader is null)
        {
            return Problem(
                title: $"The stopped trader {traderGuid} could not be found.",
                statusCode: StatusCodes.Status404NotFound
            );
        }

        return Ok(stoppedTrader);
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
        if (stoppedTrader is null)
        {
            return Problem(
                title: $"The trader {traderGuid} could not be found.",
                statusCode: StatusCodes.Status404NotFound
            );
        }

        try
        {
            await StoppedTraderRepository.Delete(stoppedTrader);

            return Ok($"The stopped trader {validatedTraderGuid} has been successfully deleted.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete stopped trader {TraderGuid}", validatedTraderGuid);
            return Problem(
                title: "Trader deletion failed.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}