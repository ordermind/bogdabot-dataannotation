using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using Akka.Actor;

namespace Bogdabot.Server.Tests.Unit.Trading.Actors.Helpers;

public class ActorTaskStoreTests
{
    private enum TaskKind
    {
        Creating,
        Starting,
        Stopping,
        Deleting,
    }

    [Fact]
    public void AddTask_ThenHasTask_ReturnsTrue()
    {
        var store = new ActorTaskStore<TaskKind>();

        var result = store.AddTask("trader-1", TaskKind.Starting, ActorRefs.Nobody, ActorRefs.Nobody);

        Assert.True(result);
        var okResult = Assert.IsType<Result<TaskKind>.Ok>(result);
        Assert.Equal(TaskKind.Starting, okResult.Value);
        Assert.True(store.HasTask("trader-1", TaskKind.Starting));
    }

    [Fact]
    public void AddTask_WhenActorAlreadyHasTask_ReturnsError()
    {
        var store = new ActorTaskStore<TaskKind>();

        store.AddTask("trader-1", TaskKind.Starting, ActorRefs.Nobody, ActorRefs.Nobody);

        var result = store.AddTask("trader-1", TaskKind.Stopping, ActorRefs.Nobody, ActorRefs.Nobody);

        Assert.False(result);
        var error = Assert.IsType<Result<TaskKind>.Error>(result);
        Assert.IsType<ActorAlreadyHasTask>(error.ErrorValue);
        Assert.Contains("trader-1", error.ErrorValue.Reason);
    }

    [Fact]
    public void RemoveTask_RemovesExistingTask()
    {
        var store = new ActorTaskStore<TaskKind>();

        store.AddTask("trader-1", TaskKind.Starting, ActorRefs.Nobody, ActorRefs.Nobody);

        var result = store.RemoveTask("trader-1", TaskKind.Starting);

        Assert.True(result);
        var okResult = Assert.IsType<Result<TaskKind>.Ok>(result);
        Assert.Equal(TaskKind.Starting, okResult.Value);
        Assert.False(store.HasTask("trader-1", TaskKind.Starting));
    }

    [Fact]
    public void RemoveActor_RemovesTaskForActor()
    {
        var store = new ActorTaskStore<TaskKind>();

        store.AddTask("trader-1", TaskKind.Starting, ActorRefs.Nobody, ActorRefs.Nobody);
        store.AddTask("trader-2", TaskKind.Deleting, ActorRefs.Nobody, ActorRefs.Nobody);

        var result = store.RemoveActor("trader-1");

        Assert.True(result);
        var okResult = Assert.IsType<Result<TaskKind>.Ok>(result);
        Assert.Equal(TaskKind.Starting, okResult.Value);
        Assert.False(store.HasTask("trader-1", TaskKind.Starting));
        Assert.True(store.HasTask("trader-2", TaskKind.Deleting));
    }

    [Fact]
    public void RemoveTask_WhenUnknown_ReturnsError()
    {
        var store = new ActorTaskStore<TaskKind>();

        var result = store.RemoveTask("trader-1", TaskKind.Starting);

        Assert.False(result);
        var error = Assert.IsType<Result<TaskKind>.Error>(result);
        Assert.IsType<ActorTaskNotFoundError>(error.ErrorValue);
    }

    [Fact]
    public void GetTask_AndGetSender_AndGetWorkflowActor_ReturnExpectedResults()
    {
        using var actorSystem = ActorSystem.Create($"actor-task-store-tests-{Guid.NewGuid()}");
        var sender = actorSystem.ActorOf(Props.Empty, "sender");
        var workflow = actorSystem.ActorOf(Props.Empty, "workflow");
        var store = new ActorTaskStore<TaskKind>();

        var addResult = store.AddTask("trader-1", TaskKind.Starting, sender, workflow);
        Assert.True(addResult);

        var taskResult = store.GetTask("trader-1");
        var senderResult = store.GetSender("trader-1", TaskKind.Starting);
        var workflowResult = store.GetWorkflowActor("trader-1", TaskKind.Starting);

        Assert.True(taskResult);
        Assert.Equal(TaskKind.Starting, Assert.IsType<Result<TaskKind>.Ok>(taskResult).Value);
        Assert.True(senderResult);
        Assert.Same(sender, Assert.IsType<Result<IActorRef>.Ok>(senderResult).Value);
        Assert.True(workflowResult);
        Assert.Same(workflow, Assert.IsType<Result<IActorRef>.Ok>(workflowResult).Value);

        var missingResult = store.GetTask("missing");
        Assert.False(missingResult);
        Assert.IsType<Result<TaskKind>.Error>(missingResult);
    }
}
