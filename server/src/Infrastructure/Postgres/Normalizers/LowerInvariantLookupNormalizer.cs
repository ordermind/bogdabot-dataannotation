using Microsoft.AspNetCore.Identity;

namespace Bogdabot.Server.Infrastructure.Postgres.Normalizers;

public class LowerInvariantLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name)
        => name?.Trim().ToLowerInvariant();

    public string? NormalizeEmail(string? email)
        => email?.Trim().ToLowerInvariant();
}