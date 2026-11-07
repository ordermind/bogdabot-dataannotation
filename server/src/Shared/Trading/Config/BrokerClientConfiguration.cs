using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;

namespace Bogdabot.Server.Shared.Trading.Config;

public record BrokerClientConfiguration
{
    public required NonEmptyGuid UserGuid { get; init; }
    public required BrokerEnum BrokerName { get; init; }

    //TODO: add specific account information to be able to connect
}