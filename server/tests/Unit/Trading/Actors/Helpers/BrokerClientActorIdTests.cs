using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Enums;

namespace Bogdabot.Server.Tests.Unit.Trading.Actors.Helpers;

public class BrokerClientActorIdTests
{
    [Fact]
    public void FromBrokerName_BuildsExpectedActorId()
    {
        Assert.Equal("brokerclient-IBKR", BrokerClientActorId.FromBrokerName(BrokerEnum.IBKR));
    }

    [Fact]
    public void TryParse_ReturnsBrokerName_WhenPrefixAndEnumAreValid()
    {
        var success = BrokerClientActorId.TryParse("brokerclient-IBKR", out var brokerName);

        Assert.True(success);
        Assert.Equal(BrokerEnum.IBKR, brokerName);
    }

    [Theory]
    [InlineData("other-IBKR")]
    [InlineData("brokerclient-not-a-broker")]
    [InlineData("brokerclient-999")]
    public void TryParse_ReturnsFalse_WhenActorIdIsInvalid(string actorId)
    {
        Assert.False(BrokerClientActorId.TryParse(actorId, out _));
    }
}
