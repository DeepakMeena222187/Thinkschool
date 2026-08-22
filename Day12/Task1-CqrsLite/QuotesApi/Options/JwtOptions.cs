using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Options;

public sealed record JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Required]
    [MinimumByteLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 bytes for HS256 signing.")]
    public string SigningKey { get; init; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(7);
}
