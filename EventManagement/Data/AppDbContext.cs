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
    public DbSet<Comment> Comments => Set<Comment>();     // <-- nuovo

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Necessario con IdentityDbContext
        base.OnModelCreating(modelBuilder);

        // -------------------- Event --------------------
        modelBuilder.Entity<Event>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            // e.Property(x => x.Title).IsRequired(); // se vuoi vincolo NotNull
        });


        // ----------------- Subscription ----------------
        modelBuilder.Entity<Subscription>(s =>
        {
            s.HasKey(x => x.Id);

            // N:1 con Event (cascade on delete)
            s.HasOne(x => x.Event)
             .WithMany(e => e.Subscriptions)
             .HasForeignKey(x => x.EventId)
             .OnDelete(DeleteBehavior.Cascade);

            // Default timestamp (SQLite). Per SQL Server: .HasDefaultValueSql("GETUTCDATE()")
            s.Property(x => x.DataSubscription)
             .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Evita doppie iscrizioni stesso utente stesso evento
            s.HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
        });

        // ------------------- Comment -------------------
        // ---- Comment configuration ----
        modelBuilder.Entity<Comment>(c =>
        {
            c.HasKey(x => x.Id);

            c.Property(x => x.Body)
             .IsRequired()
             .HasMaxLength(3000);

            // timestamp di default (SQLite). Per SQL Server -> "GETUTCDATE()"
            c.Property(x => x.CreatedAt)
             .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // La proprietà Replies è usata solo in UI per costruire l'albero
            c.Ignore(x => x.Replies);

            // Relazione -> Event (senza navigazione nel POCO)
            c.HasOne<Event>()               // principal type
             .WithMany()                    // nessuna navigazione su Event
             .HasForeignKey(x => x.EventId)
             .OnDelete(DeleteBehavior.Cascade);

            // Relazione self-reference (parent/child) senza navigazioni nel POCO
            c.HasOne<Comment>()             // parent
             .WithMany()                    // non usiamo la collezione su parent
             .HasForeignKey(x => x.ParentId)
             .OnDelete(DeleteBehavior.Cascade)
             .IsRequired(false);

            // Indici utili
            c.HasIndex(x => new { x.EventId, x.ParentId, x.CreatedAt });
        });


    }
}
