namespace Bogdabot.Server.Shared.Errors;

#region Internal errors

public sealed record BrokerClientInitializationError(string Reason) : AbstractInternalError(Reason);
public sealed record BrokerClientStartInternalError(string Reason) : AbstractInternalError(Reason);
public sealed record BrokerClientStopInternalError(string Reason) : AbstractInternalError(Reason);
public sealed record CurrentPriceNotFoundError(string Reason) : AbstractInternalError(Reason);

#endregion

#region User errors

public sealed record BrokerClientStartUserError(string Reason) : AbstractUserError(Reason);

#endregion