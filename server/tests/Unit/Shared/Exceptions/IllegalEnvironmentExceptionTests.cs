using Bogdabot.Server.Shared.Exceptions;

namespace Bogdabot.Server.Tests.Unit.Shared.Exceptions;

public class IllegalEnvironmentExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new IllegalEnvironmentException("bad env");

        Assert.Equal("bad env", ex.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndInner_SetsInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new IllegalEnvironmentException("bad env", inner);

        Assert.Same(inner, ex.InnerException);
    }
}
