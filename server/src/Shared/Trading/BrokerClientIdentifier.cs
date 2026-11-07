using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;

namespace Bogdabot.Server.Shared.Trading;

public record BrokerClientIdentifier
{
    public required NonEmptyGuid UserGuid { get; init; }
    public required BrokerEnum BrokerName { get; init; }
}