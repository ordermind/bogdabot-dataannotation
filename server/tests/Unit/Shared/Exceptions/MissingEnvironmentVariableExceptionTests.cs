using Bogdabot.Server.Shared.Exceptions;

namespace Bogdabot.Server.Tests.Unit.Shared.Exceptions;

public class MissingEnvironmentVariableExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new MissingEnvironmentVariableException("missing key");

        Assert.Equal("missing key", ex.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndInner_SetsInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new MissingEnvironmentVariableException("missing key", inner);

        Assert.Same(inner, ex.InnerException);
    }
}
