using Bogdabot.Server.Trading.Actors.Helpers;
using Akka.Actor;
using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Tests.Integration.Trading.Actors.PerUserTraderSupervisorTests.Fixtures
{
    class TestActorTaskStoreFactory : IActorTaskStoreFactory
    {
        public ActorTaskStore<TTask> Create<TTask>() where TTask : notnull
        {
            return new TestActorTaskStore<TTask>();
        }
    }

    class TestActorTaskStore<TTask> : ActorTaskStore<TTask> where TTask : notnull
    {
        public override Result<TTask> AddTask(string actorId, TTask task, IActorRef owner, IActorRef replyTo)
        {
            return new Result<TTask>.Error(new InternalError("simulated internal error"));
        }
    }
}
