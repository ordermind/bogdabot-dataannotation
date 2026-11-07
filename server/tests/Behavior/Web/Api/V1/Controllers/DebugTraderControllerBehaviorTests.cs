using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Web.Api.V1.Controllers.Debug;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Bogdabot.Server.Tests.Behavior.Web.Api.V1.Controllers;

public class DebugTraderControllerBehaviorTests
{
    [Fact]
    public async Task MigrateFresh_ReturnsOkMessage()
    {
        var events = new List<string>();
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
        var databaseManager = Substitute.For<ITraderDatabaseManager>();
        databaseManager.When(manager => manager.DeleteAllDatabases())
            .Do(_ => events.Add("delete"));
        var controller = new DebugTraderController(akkaHost, databaseManager);

        var result = await controller.MigrateFresh(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("successfully reset", ok.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "stop", "delete", "start" }, events);
        await akkaHost.Received(1).StopAsync(CancellationToken.None);
        await akkaHost.Received(1).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MigrateFresh_RestartsAkkaAndReturnsProblem_WhenDeletingDatabasesFails()
    {
        var akkaHost = Substitute.For<IAkkaHost>();
        akkaHost.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        akkaHost.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var databaseManager = Substitute.For<ITraderDatabaseManager>();
        databaseManager.When(manager => manager.DeleteAllDatabases())
            .Do(_ => throw new InvalidOperationException("delete failed"));
        var controller = new DebugTraderController(akkaHost, databaseManager);

        var result = await controller.MigrateFresh(CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, problem.StatusCode);
        await akkaHost.Received(1).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MigrateFresh_RestartsAkkaAndPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var akkaHost = Substitute.For<IAkkaHost>();
        akkaHost.StopAsync(cancellation.Token).Returns(Task.CompletedTask);
        akkaHost.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var databaseManager = Substitute.For<ITraderDatabaseManager>();
        var controller = new DebugTraderController(akkaHost, databaseManager);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.MigrateFresh(cancellation.Token));

        databaseManager.DidNotReceive().DeleteAllDatabases();
        await akkaHost.Received(1).StartAsync(CancellationToken.None);
    }
}
