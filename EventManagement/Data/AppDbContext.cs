using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EventManagement.Models;

namespace EventManagement.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Event> Events => Set<Event>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // (opzionale) chiave composta per Subscription
        builder.Entity<Subscription>()
            .HasKey(s => new { s.EventId, s.UserId });

        // 👉 quando elimini un Event, elimina anche le sue Subscriptions
        builder.Entity<Subscription>()
            .HasOne(s => s.Event)
            .WithMany(e => e.Subscriptions)
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        // relazione con IdentityUser (N:1), senza cascata sull’utente
        builder.Entity<Subscription>()
            .HasOne<IdentityUser>(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
