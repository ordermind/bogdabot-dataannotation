using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Web.Api.V1.Controllers.Debug;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Bogdabot.Server.Tests.Behavior.Web.Api.V1.Controllers;

public class DebugWebControllerBehaviorTests
{
    [Fact]
    public async Task MigrateFresh_ReturnsOk_WhenMigrationSucceeds()
    {
        var events = new List<string>();
        var migrationService = Substitute.For<IMainDbMigrationService>();
        migrationService.MigrateFresh().Returns(_ =>
        {
            events.Add("migrate");
            return Task.CompletedTask;
        });
        var akkaHost = Substitute.For<IAkkaHost>();
        akkaHost.StopAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            events.Add("stop");
            return Task.CompletedTask;
        });
        akkaHost.StartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            events.Add("start");
            return Task.CompletedTask;
        });
        var traderDatabaseManager = Substitute.For<ITraderDatabaseManager>();
        traderDatabaseManager.When(manager => manager.DeleteAllDatabases())
            .Do(_ => events.Add("delete"));
        var controller = new DebugWebController(migrationService, akkaHost, traderDatabaseManager);

        var result = await controller.MigrateFresh(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully reset", ok.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "stop", "delete", "migrate", "start" }, events);
    }

    [Fact]
    public async Task MigrateFresh_ReturnsProblem_WhenMigrationFails()
    {
        var migrationService = Substitute.For<IMainDbMigrationService>();
        migrationService.MigrateFresh().Returns(_ => throw new InvalidOperationException("boom"));
        var akkaHost = Substitute.For<IAkkaHost>();
        akkaHost.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        akkaHost.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var traderDatabaseManager = Substitute.For<ITraderDatabaseManager>();
        var controller = new DebugWebController(migrationService, akkaHost, traderDatabaseManager);

        var result = await controller.MigrateFresh(CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await akkaHost.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateFresh_RestartsAkkaAndSkipsReset_WhenStopIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var migrationService = Substitute.For<IMainDbMigrationService>();
        var akkaHost = Substitute.For<IAkkaHost>();
        akkaHost.StopAsync(cancellation.Token)
            .Returns(_ => Task.FromCanceled(cancellation.Token));
        akkaHost.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var traderDatabaseManager = Substitute.For<ITraderDatabaseManager>();
        var controller = new DebugWebController(migrationService, akkaHost, traderDatabaseManager);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.MigrateFresh(cancellation.Token));

        traderDatabaseManager.DidNotReceive().DeleteAllDatabases();
        await migrationService.DidNotReceive().MigrateFresh();
        await akkaHost.Received(1).StartAsync(CancellationToken.None);
    }
}
