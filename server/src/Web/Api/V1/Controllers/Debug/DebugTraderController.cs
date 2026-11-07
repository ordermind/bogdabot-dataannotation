using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Database;
using Microsoft.AspNetCore.Mvc;

namespace Bogdabot.Server.Web.Api.V1.Controllers.Debug;

[ApiController]
[Route("api/v1/debug/trader")]
public class DebugTraderController(IAkkaHost akkaHost, ITraderDatabaseManager traderDatabaseManager) : ControllerBase
{
    private IAkkaHost AkkaHost { get; } = akkaHost;
    private ITraderDatabaseManager TraderDatabaseManager { get; } = traderDatabaseManager;

    [HttpPost("migrate-fresh")]
    public async Task<IActionResult> MigrateFresh(CancellationToken token)
    {
        try
        {
            try
            {
                await AkkaHost.StopAsync(token);
                token.ThrowIfCancellationRequested();
                TraderDatabaseManager.DeleteAllDatabases();
            }
            finally
            {
                // The recreation of the databases will occur automatically when the actors are started up again.
                await AkkaHost.StartAsync(CancellationToken.None);
            }

            return Ok("All trader databases were successfully reset and migrations were run.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while resetting the trader databases."
            );
        }
    }
}