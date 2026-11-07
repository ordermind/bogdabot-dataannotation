namespace Bogdabot.Server.Shared.Exceptions;

class IllegalEnvironmentException : Exception
{
    public IllegalEnvironmentException()
    {
    }

    public IllegalEnvironmentException(string message)
        : base(message)
    {
    }

    public IllegalEnvironmentException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
