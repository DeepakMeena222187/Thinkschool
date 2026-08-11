using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

public sealed class AuthService(QuotesDbContext db, IConfiguration configuration)
{
    public async Task<(bool Success, string? Error, string? AccessToken, string? RefreshToken, int ExpiresIn)> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            return (false, "Invalid credentials.", null, null, 0);
        }

        var isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        if (!isValidPassword)
        {
            return (false, "Invalid credentials.", null, null, 0);
        }

        var accessToken = CreateAccessToken(user);
        var refreshToken = CreateRefreshToken();
        var refreshTokenHash = HashToken(refreshToken);

        user.RefreshTokenHash = refreshTokenHash;
        user.RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        await db.SaveChangesAsync(ct);

        return (true, null, accessToken, refreshToken, 900);
    }

    public string CreateAccessToken(User user)
    {
        var secret = GetJwtSecret(configuration);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iss, configuration["Jwt:Issuer"] ?? "https://localhost"),
            new(JwtRegisteredClaimNames.Aud, configuration["Jwt:Audience"] ?? "quotes-api")
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "https://localhost",
            audience: configuration["Jwt:Audience"] ?? "quotes-api",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public static string GetJwtSecret(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("Jwt__Secret")
            ?? throw new InvalidOperationException("JWT secret not configured. Set Jwt:Secret or the Jwt__Secret environment variable.");

        if (Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("JWT secret must be at least 32 bytes for HS256 signing.");
        }

        return secret;
    }

    public static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
