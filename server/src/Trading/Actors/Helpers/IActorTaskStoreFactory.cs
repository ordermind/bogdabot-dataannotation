namespace Bogdabot.Server.Trading.Actors.Helpers;

public interface IActorTaskStoreFactory
{
    ActorTaskStore<TTask> Create<TTask>() where TTask : notnull;
}
