namespace Bogdabot.Server.Trading.Actors.Helpers;

internal sealed class ActorTaskStoreFactory : IActorTaskStoreFactory
{
    public ActorTaskStore<TTask> Create<TTask>() where TTask : notnull
    {
        return new ActorTaskStore<TTask>();
    }
}
