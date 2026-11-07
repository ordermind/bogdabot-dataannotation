using Bogdabot.Server.Shared.Errors;
using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Tests.Unit.Shared.ValueObjects;

public class ResultTests
{
    [Fact]
    public void OkResult_ReportsSuccess_AndCanTryGetValue()
    {
        Result<int> result = new Result<int>.Ok(42);

        Assert.True(result);
        Assert.True(result.IsOk);
        Assert.False(result.IsError);
        Assert.True(result.Try(out var value));
        Assert.Equal(42, value);
        Assert.Equal(42, Assert.IsType<Result<int>.Ok>(result).Value);
    }

    [Fact]
    public void ErrorResult_ReportsFailure_AndCannotTryGetValue()
    {
        Result<int> result = new Result<int>.Error(new InternalError("boom"));

        Assert.False(result);
        Assert.False(result.IsOk);
        Assert.True(result.IsError);
        Assert.False(result.Try(out var value));
        Assert.Equal(0, value);

        var error = Assert.IsType<Result<int>.Error>(result);
        Assert.IsType<InternalError>(error.ErrorValue);
        Assert.Equal("boom", error.ErrorValue.Reason);
    }
}
