using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Author).IsRequired().HasMaxLength(100);
            entity.Property(q => q.Text).IsRequired().HasMaxLength(1000);
            entity.Property(q => q.CreatedAtUtc).IsRequired();
        });
    }
}
