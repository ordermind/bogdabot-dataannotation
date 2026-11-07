namespace Bogdabot.Server.Shared.Errors;

public sealed record ActorNotFoundError(string Reason) : AbstractInternalError(Reason);

public sealed record ActorAlreadyHasTask(string Reason)
    : AbstractInternalError(Reason);

public sealed record ActorTaskNotFoundError(string Reason)
    : AbstractInternalError(Reason);

public sealed record ActorInitializationError(string Reason) : AbstractInternalError(Reason);

public sealed record IllegalActorMessageError(string Reason) : AbstractInternalError(Reason);