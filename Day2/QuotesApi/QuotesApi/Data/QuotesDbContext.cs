using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Collection> Collections => Set<Collection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Author).IsRequired().HasMaxLength(100);
            entity.Property(q => q.Text).IsRequired().HasMaxLength(1000);
            entity.Property(q => q.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(80);
            entity.Property(c => c.OwnerId).IsRequired();
            entity.OwnsMany(c => c.Items, owned =>
            {
                owned.WithOwner().HasForeignKey("CollectionId");
                owned.Property<int>("Id");
                owned.HasKey("Id");
                owned.Property(i => i.QuoteId).IsRequired();
                owned.Property(i => i.AddedAt).IsRequired();
            });
        });
    }
}
