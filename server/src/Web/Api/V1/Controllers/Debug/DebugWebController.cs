using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Database;
using Microsoft.AspNetCore.Mvc;

namespace Bogdabot.Server.Web.Api.V1.Controllers.Debug;

[ApiController]
[Route("api/v1/debug/web")]
public class DebugWebController(IMainDbMigrationService migrationService, IAkkaHost akkaHost, ITraderDatabaseManager traderDatabaseManager) : ControllerBase
{
    private IMainDbMigrationService MigrationService { get; } = migrationService;
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
                await MigrationService.MigrateFresh();
            }
            finally
            {
                await AkkaHost.StartAsync(CancellationToken.None);
            }

            return Ok("The database was successfully reset and migrations were run.");
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
                detail: "An unexpected error occurred while resetting the database."
            );
        }
    }
}