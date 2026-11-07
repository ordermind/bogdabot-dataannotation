namespace Bogdabot.Server.Tests.TestSupport;

internal static class ProcessStateLock
{
    private static readonly object Sync = new();

    public static void Run(Action action)
    {
        lock (Sync)
        {
            action();
        }
    }
}
