namespace Bogdabot.Shared.Admin;

public sealed record ActorOperationResult(
    string ActorId,
    bool Success,
    string? Error = null
);