using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace QuotesApi.Tests;

internal static class TestTokens
{
    public const string InternalIssuer = "https://localhost";
    public const string InternalAudience = "quotes-api";

    public static string CreateInternalToken(int userId, params Claim[] extraClaims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString())
        };
        claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            issuer: InternalIssuer,
            audience: InternalAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
