using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Trading.Actors.Helpers;

public static class TraderActorId
{
    private const string Prefix = "trader";

    public static string FromTraderGuid(NonEmptyGuid traderGuid) =>
        ActorId.FromGuid(Prefix, traderGuid);

    public static bool TryParse(
        string actorId,
        out NonEmptyGuid traderGuid) =>
        ActorId.TryParse(Prefix, actorId, out traderGuid);
}
