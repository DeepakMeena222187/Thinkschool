using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Quotes.Tests.Integration.Testcontainers;

internal static class TestAuth
{
    public const string InternalIssuer = "https://localhost";
    public const string InternalAudience = "quotes-api";

    public static string CreateInternalToken(int userId, params Claim[] extraClaims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IntegrationTestFactory.TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
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

    public static string CreateExpiredInternalToken(int userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IntegrationTestFactory.TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: InternalIssuer,
            audience: InternalAudience,
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) },
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
