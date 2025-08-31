using EventManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace EventManagement.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Event> Events => Set<Event>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Un utente può iscriversi una sola volta ad un evento
        modelBuilder.Entity<Subscription>()
            .HasIndex(s => new { s.EventId, s.UserId })
            .IsUnique();

        modelBuilder.Entity<Subscription>()
            .HasOne(s => s.Event)
            .WithMany(e => e.Subscriptions)
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Subscription>()
            .HasOne(s => s.User)
            .WithMany(u => u.Subscriptions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
