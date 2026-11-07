using Bogdabot.Server.Trading.Enums;

namespace Bogdabot.Server.Trading.Actors.Helpers;

public static class BrokerClientActorId
{
    private const string Prefix = "brokerclient";

    public static string FromBrokerName(BrokerEnum brokerName) =>
        ActorId.FromEnum(Prefix, brokerName);

    public static bool TryParse(
        string actorId,
        out BrokerEnum brokerName) =>
        ActorId.TryParse(Prefix, actorId, out brokerName);
}
