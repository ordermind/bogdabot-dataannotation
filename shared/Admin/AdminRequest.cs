namespace Bogdabot.Shared.Admin;

public sealed record AdminRequest(
    string Command,
    string? ActorId = null
);