using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;

namespace Bogdabot.Server.Tests.Unit.Trading.Actors.Helpers;

public class TraderActorIdTests
{
    [Fact]
    public void FromGuid_BuildsExpectedActorId()
    {
        var guid = NonEmptyGuid.NewGuid();

        var id = TraderActorId.FromTraderGuid(guid);

        Assert.Equal($"trader-{guid}", id);
    }

    [Fact]
    public void TryParse_ReturnsTrueAndGuid_WhenPrefixAndGuidAreValid()
    {
        var guid = NonEmptyGuid.NewGuid();

        var success = TraderActorId.TryParse($"trader-{guid}", out var parsedGuid);

        Assert.True(success);
        Assert.Equal(guid, parsedGuid);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenPrefixDoesNotMatch()
    {
        var success = TraderActorId.TryParse($"user-{Guid.NewGuid()}", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenGuidIsInvalid()
    {
        var success = TraderActorId.TryParse("trader-not-a-guid", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenGuidIsEmpty()
    {
        var success = TraderActorId.TryParse("trader-00000000-0000-0000-0000-000000000000", out _);

        Assert.False(success);
    }
}
