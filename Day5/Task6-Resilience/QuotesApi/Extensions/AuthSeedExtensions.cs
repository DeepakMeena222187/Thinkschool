using BCrypt.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Extensions;

public static class AuthSeedExtensions
{
    // Was missing the environment guard its sibling (CollectionSeedExtensions.
    // SeedDevelopmentCollectionsAsync) already had - meant this ran in every
    // environment, including Production, seeding a hardcoded-password login
    // into whatever database it's pointed at. Guarded the same way now (Day 17).
    // QUOTES_API_DEV_EMAIL/QUOTES_API_DEV_PASSWORD have no literal fallback -
    // if either is unset, skip seeding rather than fall back to a known password.
    public static async Task SeedDevelopmentUserAsync(this IServiceProvider services, IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var email = Environment.GetEnvironmentVariable("QUOTES_API_DEV_EMAIL");
        var password = Environment.GetEnvironmentVariable("QUOTES_API_DEV_PASSWORD");
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

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
