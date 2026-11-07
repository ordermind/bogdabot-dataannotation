namespace Bogdabot.Server.Shared.Errors;

#region Internal errors

public sealed record FailedConfigFetchError(string Reason) : AbstractInternalError(Reason);
public sealed record FailedStatusFetchError(string Reason) : AbstractInternalError(Reason);
public sealed record FailedFetchingTraderSummaryError(string Reason) : AbstractInternalError(Reason);
public sealed record FailedPersistingStoppedTraderError(string Reason) : AbstractInternalError(Reason);
public sealed record FailedDatabaseCleanupError(string Reason) : AbstractInternalError(Reason);
public sealed record FailedDatabaseMigrationsError(string Reason) : AbstractInternalError(Reason);
public sealed record FailedDatabaseReadError(string Reason) : AbstractInternalError(Reason);
public sealed record FailedDatabaseWriteError(string Reason) : AbstractInternalError(Reason);
public sealed record TraderActorNotFoundError(string Reason) : AbstractInternalError(Reason);
public sealed record TraderInitializationError(string Reason) : AbstractInternalError(Reason);
public sealed record TraderStartInternalError(string Reason) : AbstractInternalError(Reason);
public sealed record TraderStopInternalError(string Reason) : AbstractInternalError(Reason);
public sealed record TraderDeleteInternalError(string Reason) : AbstractInternalError(Reason);

#endregion

#region User errors

public sealed record IllegalStatusTransitionError(string Reason) : AbstractUserError(Reason);
public sealed record TraderSupervisorStartUserError(string Reason) : AbstractUserError(Reason);
public sealed record TraderSupervisorStopUserError(string Reason) : AbstractUserError(Reason);
public sealed record TraderStartUserError(string Reason) : AbstractUserError(Reason);
public sealed record TraderStopUserError(string Reason) : AbstractUserError(Reason);
public sealed record TraderDeleteUserError(string Reason) : AbstractUserError(Reason);

#endregion