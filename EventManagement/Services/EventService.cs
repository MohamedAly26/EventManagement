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
    public EventService(AppDbContext db) => _db = db;

    // -------------------- Lettura Eventi --------------------

    public async Task<List<Event>> GetAllAsync()
     => await _db.Events
         .AsNoTracking()
         .Include(e => e.Subscriptions)
         .OrderBy(e => e.StartDateTime)
         .ToListAsync();

    public async Task<Event?> GetByIdAsync(int id) =>
        await _db.Events
            .AsNoTracking()
            .Include(e => e.Subscriptions)
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<List<string>> GetCategoriesAsync() =>
        await _db.Events
            .AsNoTracking()
            .Where(e => !string.IsNullOrWhiteSpace(e.Category))
            .Select(e => e.Category!) // safe: filtrato sopra
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

    public async Task<List<Event>> SearchAsync(
        string? q,
        string? category,
        DateTime? from,
        DateTime? to,
        string? location)
    {
        var query = _db.Events
            .AsNoTracking()
            .Include(e => e.Subscriptions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var l = q.ToLower();
            query = query.Where(e =>
                (e.Title != null && e.Title.ToLower().Contains(l)) ||
                (e.Description != null && e.Description.ToLower().Contains(l)) ||
                (e.Location != null && e.Location.ToLower().Contains(l)));
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(e => e.Category == category);

        if (from.HasValue)
            query = query.Where(e => e.StartDateTime >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.StartDateTime <= to.Value);

        if (!string.IsNullOrWhiteSpace(location))
        {
            var ll = location.ToLower();
            query = query.Where(e => e.Location != null && e.Location.ToLower().Contains(ll));
        }

        return await query.OrderBy(e => e.StartDateTime).ToListAsync();
    }

    // -------------------- Iscrizioni --------------------

    public Task<bool> IsSubscribedAsync(int eventId, string userId) =>
        _db.Subscriptions.AnyAsync(s => s.EventId == eventId && s.UserId == userId);

    public async Task<SubscribeResult> SubscribeAsync(int eventId, string userId)
    {
        // keep the checks as-is...
        var ev = await _db.Events
            .AsNoTracking()
            .Select(e => new { e.Id, e.MaxParticipants })
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (ev is null) return SubscribeResult.EventNotFound;

        var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists) return SubscribeResult.UserNotFound;

        if (await IsSubscribedAsync(eventId, userId))
            return SubscribeResult.AlreadySubscribed;

        var currentCount = await _db.Subscriptions.CountAsync(s => s.EventId == eventId);
        if (currentCount >= ev.MaxParticipants)
            return SubscribeResult.EventFull;

        // ✅ set DataSubscription so NOT NULL is satisfied
        _db.Subscriptions.Add(new Subscription
        {
            EventId = eventId,
            UserId = userId,
            DataSubscription = DateTime.UtcNow   // <- important
        });

        await _db.SaveChangesAsync();
        return SubscribeResult.Success;
    }


    public async Task<bool> UnsubscribeAsync(int eventId, string userId)
    {
        var sub = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);
        if (sub is null) return false;

        _db.Subscriptions.Remove(sub);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<Event>> GetUserSubscriptionsAsync(string userId, bool includePast = false)
    {
        var query = _db.Events
            .AsNoTracking()
            .Where(e => e.Subscriptions!.Any(s => s.UserId == userId))
            .Include(e => e.Subscriptions)
            .AsQueryable();

        if (!includePast)
            query = query.Where(e => e.StartDateTime >= DateTime.Now);

        return await query
            .OrderBy(e => e.StartDateTime)
            .ToListAsync();
    }

    public record EventSubscriber(string UserId, string? Email, string? UserName, DateTime CreatedAt);

    public async Task<List<EventSubscriber>> GetSubscribersForEventAsync(int eventId) =>
        await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.EventId == eventId)
            .Include(s => s.User)
            .OrderBy(s => s.DataSubscription) // <-- proprietà attuale del model
            .Select(s => new EventSubscriber(
                s.UserId,
                s.User != null ? s.User.Email : null,
                s.User != null ? s.User.UserName : null,
                s.DataSubscription))
            .ToListAsync();

    // -------------------- CRUD Eventi (Admin) --------------------

    public async Task<Event> CreateAsync(Event ev)
    {
        _db.Events.Add(ev);
        await _db.SaveChangesAsync();
        return ev;
    }

    public async Task<bool> UpdateAsync(Event ev)
    {
        var dbEv = await _db.Events.FindAsync(ev.Id);
        if (dbEv is null) return false;

        _db.Entry(dbEv).CurrentValues.SetValues(ev);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var ev = await _db.Events.FindAsync(id);
        if (ev is null) return false;

        // Subscriptions verranno rimosse in CASCADE (configurato nel DbContext)
        _db.Events.Remove(ev);
        await _db.SaveChangesAsync();
        return true;
    }

    // -------------------- Stats & quick summaries --------------------

    public record EventStats(int TotalEvents, int Upcoming, int TotalSubscriptions, int SeatsCapacity, int SeatsTaken);
    public record EventSummary(int Id, string Title, DateTime StartDateTime, int SubsCount, int MaxParticipants);

    public async Task<EventStats> GetBasicStatsAsync()
    {
        var now = DateTime.Now;

        var totalEventsTask = _db.Events.CountAsync();
        var upcomingTask = _db.Events.CountAsync(e => e.StartDateTime >= now);
        var totalSubsTask = _db.Subscriptions.CountAsync();
        var capacityTask = _db.Events.SumAsync(e => e.MaxParticipants);

        await Task.WhenAll(totalEventsTask, upcomingTask, totalSubsTask, capacityTask);

        return new EventStats(
            totalEventsTask.Result,
            upcomingTask.Result,
            totalSubsTask.Result,
            capacityTask.Result,
            totalSubsTask.Result   // posti occupati = totale iscrizioni
        );
    }

    public async Task<List<EventSummary>> GetUpcomingSummariesAsync(int take = 5)
    {
        // hardening parametro
        if (take < 1) take = 1;
        if (take > 50) take = 50;

        var now = DateTime.Now;

        return await _db.Events
            .AsNoTracking()
            .Where(e => e.StartDateTime >= now)
            .OrderBy(e => e.StartDateTime)
           .Select(e => new EventSummary(
                e.Id,
                e.Title ?? "(untitled)",
                e.StartDateTime,
                e.Subscriptions!.Count(),   // nav prop is loaded by EF in the projection
                e.MaxParticipants
                ))
            .Take(take)
            .ToListAsync();
    }
    public record AdminEventRow(int Id, string Title, DateTime StartDateTime, string? Location, int Subs, int MaxParticipants);

    public Task<List<AdminEventRow>> GetAdminEventRowsAsync() =>
        _db.Events
           .AsNoTracking()
           .OrderBy(e => e.StartDateTime)
           .Select(e => new AdminEventRow(
               e.Id,
               e.Title ?? "(untitled)",
               e.StartDateTime,
               e.Location,
               e.Subscriptions!.Count(),   // conteggio in SQL, senza Include
               e.MaxParticipants))
           .ToListAsync();

}
