using Akka.Actor;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Trading.Actors.Helpers;

public readonly record struct TaskActors(
    IActorRef Sender,
    IActorRef WorkflowActor
);

/// <summary>
/// Tracks tasks keyed by actor id. Only one task at a time per actor id is allowed.
/// </summary>
public class ActorTaskStore<TTask>
    where TTask : notnull
{
    private readonly Dictionary<string, TaskEntry> _tasksByActorId = [];

    private sealed record TaskEntry(
        TTask Task,
        TaskActors Actors);

    public virtual Result<TTask> AddTask(
        string actorId,
        TTask task,
        IActorRef sender,
        IActorRef workflowActor)
    {
        if (_tasksByActorId.TryGetValue(actorId, out var existingTask))
        {
            return new Result<TTask>.Error(
                new ActorAlreadyHasTask(
                    $"There already exists a task for Actor '{actorId}': {existingTask.Task.GetType()}."));
        }

        _tasksByActorId[actorId] = new TaskEntry(
            task,
            new TaskActors(sender, workflowActor)
        );

        return new Result<TTask>.Ok(task);
    }

    public bool HasTask(string actorId)
    {
        return _tasksByActorId.ContainsKey(actorId);
    }

    public bool HasTask(string actorId, TTask task)
    {
        return _tasksByActorId.TryGetValue(actorId, out var entry)
            && EqualityComparer<TTask>.Default.Equals(entry.Task, task);
    }

    public bool HasTaskOf(string actorId, IEnumerable<TTask> tasks)
    {
        return _tasksByActorId.TryGetValue(actorId, out var entry)
            && tasks.Contains(entry.Task);
    }

    public Result<TTask> GetTask(string actorId)
    {
        return _tasksByActorId.TryGetValue(actorId, out var entry)
            ? new Result<TTask>.Ok(entry.Task)
            : new Result<TTask>.Error(
                new ActorTaskNotFoundError(
                    $"Actor '{actorId}' does not have a task."));
    }

    public Result<TaskActors> GetTaskActors(string actorId, TTask task)
    {
        if (_tasksByActorId.TryGetValue(actorId, out var entry)
            && EqualityComparer<TTask>.Default.Equals(entry.Task, task))
        {
            return new Result<TaskActors>.Ok(entry.Actors);
        }

        return new Result<TaskActors>.Error(
            new ActorTaskNotFoundError(
                $"Task for actor '{actorId}' was not found."));
    }

    public Result<IActorRef> GetSender(string actorId, TTask task)
    {
        return GetTaskActors(actorId, task) switch
        {
            Result<TaskActors>.Ok(var actors) when !actors.Sender.IsNobody()
                => new Result<IActorRef>.Ok(actors.Sender),

            Result<TaskActors>.Ok
                => new Result<IActorRef>.Error(
                    new ActorTaskNotFoundError(
                        $"Sender for task of actor '{actorId}' was not found.")),

            Result<TaskActors>.Error(var error)
                => new Result<IActorRef>.Error(
                    new ActorTaskNotFoundError(
                        $"Failed to get sender for task of actor '{actorId}': {error.Reason}")),

            _ => new Result<IActorRef>.Error(new InternalError("Unknown error"))
        };
    }

    public Result<IActorRef> GetWorkflowActor(string actorId, TTask task)
    {
        return GetTaskActors(actorId, task) switch
        {
            Result<TaskActors>.Ok(var actors) when !actors.WorkflowActor.IsNobody()
                => new Result<IActorRef>.Ok(actors.WorkflowActor),

            Result<TaskActors>.Ok
                => new Result<IActorRef>.Error(
                    new ActorTaskNotFoundError(
                        $"Workflow actor for task of actor '{actorId}' was not found.")),

            Result<TaskActors>.Error(var error)
                => new Result<IActorRef>.Error(
                    new ActorTaskNotFoundError(
                        $"Failed to get workflow actor for task of actor '{actorId}': {error.Reason}")),

            _ => new Result<IActorRef>.Error(new InternalError("Unknown error"))
        };
    }

    public Result<TTask> RemoveTask(string actorId)
    {
        if (!_tasksByActorId.TryGetValue(actorId, out var entry))
        {
            return new Result<TTask>.Error(
                new ActorTaskNotFoundError(
                    $"Task for actor '{actorId}' was not found."));
        }

        _tasksByActorId.Remove(actorId);

        return new Result<TTask>.Ok(entry.Task);
    }

    public Result<TTask> RemoveTask(string actorId, TTask task)
    {
        if (!_tasksByActorId.TryGetValue(actorId, out var entry)
            || !EqualityComparer<TTask>.Default.Equals(entry.Task, task))
        {
            return new Result<TTask>.Error(
                new ActorTaskNotFoundError(
                    $"Task for actor '{actorId}' was not found."));
        }

        _tasksByActorId.Remove(actorId);

        return new Result<TTask>.Ok(entry.Task);
    }

    public Result<TTask> RemoveActor(string actorId)
    {
        if (!_tasksByActorId.Remove(actorId, out var entry))
        {
            return new Result<TTask>.Error(
                new ActorTaskNotFoundError(
                    $"Actor '{actorId}' does not have a task."));
        }

        return new Result<TTask>.Ok(entry.Task);
    }

    public bool IsEmpty()
    {
        return _tasksByActorId.Count == 0;
    }
}