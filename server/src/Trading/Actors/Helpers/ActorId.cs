using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Trading.Actors.Helpers;

public static class ActorId
{
    public static string FromGuid(string prefix, NonEmptyGuid guid)
    {
        return $"{prefix}-{guid}";
    }

    public static string FromEnum<T>(string prefix, T value) where T : struct, Enum
    {
        return $"{prefix}-{value}";
    }

    public static bool TryParse(
        string prefix,
        string actorId,
        out NonEmptyGuid guid)
    {
        guid = default;

        var fullPrefix = $"{prefix}-";

        if (!actorId.StartsWith(fullPrefix, StringComparison.Ordinal))
            return false;

        var substring = actorId[fullPrefix.Length..];

        if (!Guid.TryParse(substring, out var value))
            return false;

        if (value == Guid.Empty)
            return false;

        guid = new NonEmptyGuid(value);
        return true;
    }

    public static bool TryParse<T>(
        string prefix,
        string actorId,
        out T enumValue)
        where T : struct, Enum
    {
        enumValue = default;

        var fullPrefix = $"{prefix}-";

        if (!actorId.StartsWith(fullPrefix, StringComparison.Ordinal))
            return false;

        var substring = actorId[fullPrefix.Length..];

        if (!Enum.TryParse<T>(substring, out var parsedValue))
            return false;

        if (!Enum.IsDefined(parsedValue))
            return false;

        enumValue = parsedValue;
        return true;
    }
}