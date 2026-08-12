using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Extensions;

public static class AuthSeedExtensions
{
    public static async Task SeedDevelopmentUserAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var email = Environment.GetEnvironmentVariable("QUOTES_API_DEV_EMAIL") ?? "admin@quotes.local";
        var password = Environment.GetEnvironmentVariable("QUOTES_API_DEV_PASSWORD") ?? "meena@123";

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return;
        }

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
