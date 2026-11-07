namespace Bogdabot.Server.Shared.Errors;

public abstract record AbstractError(string Reason)
{
    public static implicit operator string(AbstractError error) => error.Reason;
}

public abstract record AbstractInternalError(string Reason) : AbstractError(Reason);
public abstract record AbstractUserError(string Reason) : AbstractError(Reason);