using EventManagement.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventManagement.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Event> Events => Set<Event>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Required when inheriting from IdentityDbContext
        base.OnModelCreating(modelBuilder);

        // ---- Event configuration ----
        modelBuilder.Entity<Event>(e =>
        {
            e.Property(x => x.Title)
             .HasMaxLength(200);
            // Add other constraints if you wish (e.g., .IsRequired())
        });

        // ---- Subscription configuration ----
        modelBuilder.Entity<Subscription>(s =>
        {
            // Key is inferred from Id, but we can be explicit
            s.HasKey(x => x.Id);

            // Many Subscriptions -> One Event (cascade on delete)
            s.HasOne(x => x.Event)
             .WithMany(e => e.Subscriptions)
             .HasForeignKey(x => x.EventId)
             .OnDelete(DeleteBehavior.Cascade);

            // Default timestamp for SQLite; for SQL Server use: GETUTCDATE()
            s.Property(x => x.DataSubscription)
             .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Prevent duplicate subscriptions for the same user on the same event
            s.HasIndex(x => new { x.EventId, x.UserId })
             .IsUnique();
        });
    }
}
