namespace Bogdabot.Server.Shared.Errors;

public sealed record InternalError(string Reason)
    : AbstractInternalError(Reason);