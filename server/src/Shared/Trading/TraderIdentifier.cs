using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Shared.Trading;

public record TraderIdentifier
{
    public required NonEmptyGuid UserGuid { get; init; }
    public required NonEmptyGuid TraderGuid { get; init; }
}