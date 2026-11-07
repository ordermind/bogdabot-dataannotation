namespace Bogdabot.Shared.Admin;

public sealed record AdminResponse(
    bool Success,
    string? Error = null,
    IReadOnlyList<ActorOperationResult>? Results = null
);