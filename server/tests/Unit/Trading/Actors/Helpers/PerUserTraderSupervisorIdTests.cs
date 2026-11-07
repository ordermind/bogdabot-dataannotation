using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;

namespace Bogdabot.Server.Tests.Unit.Trading.Actors.Helpers;

public class PerUserTraderSupervisorIdTests
{
    [Fact]
    public void FromGuid_BuildsExpectedActorId()
    {
        var guid = NonEmptyGuid.NewGuid();

        var id = PerUserTraderSupervisorId.FromUserGuid(guid);

        Assert.Equal($"tradersupervisor-{guid}", id);
    }

    [Fact]
    public void TryParse_ReturnsTrueAndGuid_WhenPrefixAndGuidAreValid()
    {
        var guid = NonEmptyGuid.NewGuid();

        var success = PerUserTraderSupervisorId.TryParse($"tradersupervisor-{guid}", out var parsedGuid);

        Assert.True(success);
        Assert.Equal(guid, parsedGuid);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenPrefixDoesNotMatch()
    {
        var success = PerUserTraderSupervisorId.TryParse($"trader-{Guid.NewGuid()}", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenGuidIsInvalid()
    {
        var success = PerUserTraderSupervisorId.TryParse("user-not-a-guid", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenGuidIsEmpty()
    {
        var success = PerUserTraderSupervisorId.TryParse("user-00000000-0000-0000-0000-000000000000", out _);

        Assert.False(success);
    }
}
