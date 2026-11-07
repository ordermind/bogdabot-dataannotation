using Bogdabot.Server.Infrastructure.Postgres.Normalizers;

namespace Bogdabot.Server.Tests.Unit.Infrastructure.Postgres.Normalizers;

public class LowerInvariantLookupNormalizerTests
{
    [Fact]
    public void NormalizeName_TrimAndLowercase()
    {
        var normalizer = new LowerInvariantLookupNormalizer();

        var value = normalizer.NormalizeName("  HeLLo  ");

        Assert.Equal("hello", value);
    }

    [Fact]
    public void NormalizeEmail_TrimAndLowercase()
    {
        var normalizer = new LowerInvariantLookupNormalizer();

        var value = normalizer.NormalizeEmail("  USER@EXAMPLE.COM  ");

        Assert.Equal("user@example.com", value);
    }
}
