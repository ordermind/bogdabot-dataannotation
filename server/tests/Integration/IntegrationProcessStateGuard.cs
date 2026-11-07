using Bogdabot.Server.Tests.TestSupport;

namespace Bogdabot.Server.Tests.Integration;

internal static class IntegrationProcessStateGuard
{
    public static void Run(Action action)
    {
        ProcessStateLock.Run(action);
    }
}
