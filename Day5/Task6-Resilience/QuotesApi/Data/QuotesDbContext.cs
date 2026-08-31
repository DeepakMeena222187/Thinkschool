using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EventLog> EventLogs => Set<EventLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Author).IsRequired().HasMaxLength(100);
            entity.Property(q => q.Text).IsRequired().HasMaxLength(1000);
            entity.Property(q => q.CreatedAtUtc).IsRequired();
            entity.Property(q => q.OwnerId).IsRequired();
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

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(255);
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(rt => rt.Id);
            entity.Property(rt => rt.TokenHash).IsRequired();
            entity.Property(rt => rt.FamilyId).IsRequired().HasMaxLength(64);
            entity.Property(rt => rt.CreatedAtUtc).IsRequired();
            entity.Property(rt => rt.ExpiresAtUtc).IsRequired();
            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EventLog already exists in the live database (Day 8-9 index/isolation
        // exercises) and isn't owned by this project's migrations. Excluding it
        // tells EF's migration differ this table's DDL lifecycle is external, so
        // `dotnet ef migrations add` never emits a CreateTable/DropTable for it.
        modelBuilder.Entity<EventLog>(entity =>
        {
            entity.ToTable("EventLog", t => t.ExcludeFromMigrations());
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.Payload).IsRequired().HasMaxLength(200);
        });
    }
}
