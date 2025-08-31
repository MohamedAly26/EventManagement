using EventManagement.Data;
using EventManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace EventManagement.Services;

public enum SubscribeResult
{
    Success,
    AlreadySubscribed,
    EventNotFound,
    EventFull,
    UserNotFound
}

public class EventService
{
    private readonly AppDbContext _db;

    public EventService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Event>> GetAllAsync()
    {
        return await _db.Events
            .Include(e => e.Subscriptions)
            .OrderBy(e => e.StartDateTime)
            .ToListAsync();
    }

    public async Task<Event?> GetByIdAsync(int id)
    {
        return await _db.Events
            .Include(e => e.Subscriptions)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<SubscribeResult> SubscribeAsync(int eventId, int userId)
    {
        var ev = await _db.Events.Include(e => e.Subscriptions)
                                 .FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev == null) return SubscribeResult.EventNotFound;

        var user = await _db.Users.FindAsync(userId);
        if (user == null) return SubscribeResult.UserNotFound;

        // già iscritto?
        var already = await _db.Subscriptions
            .AnyAsync(s => s.EventId == eventId && s.UserId == userId);
        if (already) return SubscribeResult.AlreadySubscribed;

        // capienza piena?
        var current = ev.Subscriptions?.Count ?? 0;
        if (current >= ev.MaxParticipants) return SubscribeResult.EventFull;

        _db.Subscriptions.Add(new Subscription
        {
            EventId = eventId,
            UserId = userId
        });

        await _db.SaveChangesAsync();
        return SubscribeResult.Success;
    }



    public async Task<bool> IsSubscribedAsync(int eventId, int userId)
    {
        return await _db.Subscriptions
            .AnyAsync(s => s.EventId == eventId && s.UserId == userId);
    }

    public async Task<bool> UnsubscribeAsync(int eventId, int userId)
    {
        var sub = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);
        if (sub is null) return false;

        _db.Subscriptions.Remove(sub);
        await _db.SaveChangesAsync();
        return true;
    }

    // Utente demo (finché non mettiamo l’auth)
    public async Task<int?> GetAnyUserIdAsync()
    {
        return await _db.Users
            .OrderBy(u => u.Id)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync();
    }
}
