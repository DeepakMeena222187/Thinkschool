using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Options;

namespace QuotesApi.Services;

public enum RefreshTokenStatus
{
    Valid,
    Invalid,
    Reused
}

public sealed class AuthService(QuotesDbContext db, IOptions<JwtOptions> jwtOptions, IClock clock, ILogger<AuthService> logger)
{
    public RefreshTokenStatus EvaluateRefreshTokenStatus(RefreshToken token)
    {
        if (token.RevokedAtUtc is not null || token.ExpiresAtUtc <= clock.UtcNow.UtcDateTime)
        {
            return RefreshTokenStatus.Invalid;
        }

        if (!string.IsNullOrWhiteSpace(token.ReplacedByTokenHash))
        {
            return RefreshTokenStatus.Reused;
        }

        return RefreshTokenStatus.Valid;
    }

    public async Task<(bool Success, string? Error, string? AccessToken, string? RefreshToken, int ExpiresIn)> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            logger.LogWarning("Login failed: no user found for {Email}", email);
            return (false, "Invalid credentials.", null, null, 0);
        }

        var isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        if (!isValidPassword)
        {
            logger.LogWarning("Login failed for UserId={UserId}: invalid password", user.Id);
            return (false, "Invalid credentials.", null, null, 0);
        }

        var accessToken = CreateAccessToken(user);
        var refreshToken = CreateRefreshToken();
        var refreshTokenHash = HashToken(refreshToken);
        var familyId = Guid.NewGuid().ToString("N");

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            FamilyId = familyId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(jwtOptions.Value.RefreshTokenLifetime)
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Login succeeded for UserId={UserId}", user.Id);

        return (true, null, accessToken, refreshToken, (int)jwtOptions.Value.AccessTokenLifetime.TotalSeconds);
    }

    public async Task<(bool Success, string? Error, string? AccessToken, string? RefreshToken, int ExpiresIn)> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return (false, "Refresh token is required.", null, null, 0);
        }

        var tokenHash = HashToken(refreshToken);
        var currentToken = await db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (currentToken is null)
        {
            return (false, "Invalid refresh token.", null, null, 0);
        }

        var status = EvaluateRefreshTokenStatus(currentToken);
        if (status == RefreshTokenStatus.Invalid)
        {
            return (false, "Refresh token is no longer valid.", null, null, 0);
        }

        if (status == RefreshTokenStatus.Reused)
        {
            var familyTokens = await db.RefreshTokens
                .Where(rt => rt.FamilyId == currentToken.FamilyId)
                .ToListAsync(ct);

            foreach (var familyToken in familyTokens)
            {
                familyToken.RevokedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return (false, "Refresh token has already been used.", null, null, 0);
        }

        var newRefreshToken = CreateRefreshToken();
        var newRefreshTokenHash = HashToken(newRefreshToken);

        currentToken.ReplacedByTokenHash = newRefreshTokenHash;
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = currentToken.UserId,
            TokenHash = newRefreshTokenHash,
            FamilyId = currentToken.FamilyId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(jwtOptions.Value.RefreshTokenLifetime)
        });

        await db.SaveChangesAsync(ct);

        var accessToken = CreateAccessToken(currentToken.User);
        return (true, null, accessToken, newRefreshToken, (int)jwtOptions.Value.AccessTokenLifetime.TotalSeconds);
    }

    public async Task<bool> LogoutAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var tokenHash = HashToken(refreshToken);
        var currentToken = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);
        if (currentToken is null)
        {
            return false;
        }

        currentToken.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public string CreateAccessToken(User user)
    {
        var jwt = jwtOptions.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iss, jwt.Issuer),
            new(JwtRegisteredClaimNames.Aud, jwt.Audience),
            new(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope)
        };

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(jwt.AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
