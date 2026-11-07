using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Trading.Actors.Helpers;

public static class WorkflowActorId
{
    private const string Prefix = "workflow";

    public static string FromGuid(NonEmptyGuid guid) =>
        ActorId.FromGuid(Prefix, guid);

    public static bool TryParse(
        string actorId,
        out NonEmptyGuid guid) =>
        ActorId.TryParse(Prefix, actorId, out guid);
}
