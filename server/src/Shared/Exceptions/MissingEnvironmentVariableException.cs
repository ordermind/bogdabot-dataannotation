namespace Bogdabot.Server.Shared.Exceptions;

class MissingEnvironmentVariableException : Exception
{
    public MissingEnvironmentVariableException()
    {
    }

    public MissingEnvironmentVariableException(string message)
        : base(message)
    {
    }

    public MissingEnvironmentVariableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
