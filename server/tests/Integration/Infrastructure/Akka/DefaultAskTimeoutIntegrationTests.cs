using Akka.Actor;
using System.Diagnostics;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Akka;

public class DefaultAskTimeoutIntegrationTests
{
    [Fact]
    public async Task ActorSystem_UsesConfiguredDefaultAskTimeout()
    {
        using var actorSystem = ActorSystem.Create($"default-ask-timeout-tests-{Guid.NewGuid()}");

        var askTimeout = actorSystem.Settings.Config.GetTimeSpan("akka.actor.ask-timeout");

        Assert.True(askTimeout > TimeSpan.Zero);

        await actorSystem.Terminate();
    }

    [Fact]
    public async Task AskWithoutExplicitTimeout_ThrowsAfterDefaultAskTimeout()
    {
        using var actorSystem = ActorSystem.Create($"default-ask-timeout-tests-{Guid.NewGuid()}");
        var actor = actorSystem.ActorOf(Props.Create(() => new NeverReplyActor()));
        var askTimeout = actorSystem.Settings.Config.GetTimeSpan("akka.actor.ask-timeout");
        var timingTolerance = TimeSpan.FromMilliseconds(Math.Max(100, askTimeout.TotalMilliseconds * 0.25));
        var earliestExpectedTimeout = TimeSpan.FromTicks(Math.Max(0, askTimeout.Ticks - timingTolerance.Ticks));
        var latestExpectedTimeout = askTimeout + timingTolerance;
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<AskTimeoutException>(() =>
            actor.Ask<object>("request", TestContext.Current.CancellationToken));

        stopwatch.Stop();
        Assert.InRange(stopwatch.Elapsed, earliestExpectedTimeout, latestExpectedTimeout);

        await actorSystem.Terminate();
    }

    private sealed class NeverReplyActor : ReceiveActor
    {
        public NeverReplyActor()
        {
            ReceiveAny(_ => { });
        }
    }
}
