using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Trading.Actors.Helpers;

public static class PerUserTraderSupervisorId
{
    private const string Prefix = "tradersupervisor";

    public static string FromUserGuid(NonEmptyGuid userGuid) =>
        ActorId.FromGuid(Prefix, userGuid);

    public static bool TryParse(
        string actorId,
        out NonEmptyGuid userGuid) =>
        ActorId.TryParse(Prefix, actorId, out userGuid);
}
