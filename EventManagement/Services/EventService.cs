using EventManagement.Data;
using EventManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace EventManagement.Services
{
    public class EventService
    {
        private readonly AppDbContext _context;

        public EventService(AppDbContext context)
        {
            _context = context;
        }

        // Get all events (ordered by date)
        public async Task<List<Event>> GetAllAsync()
        {
            return await _context.Events
                .OrderBy(e => e.StartDateTime)
                .ToListAsync();
        }

        // Get event by id
        public async Task<Event?> GetByIdAsync(int id)
        {
            return await _context.Events
                .Include(e => e.Subscriptions)
                .FirstOrDefaultAsync(e => e.Id == id);
        }

        // Add new event
        public async Task AddAsync(Event ev)
        {
            _context.Events.Add(ev);
            await _context.SaveChangesAsync();
        }

        // Update event
        public async Task UpdateAsync(Event ev)
        {
            _context.Events.Update(ev);
            await _context.SaveChangesAsync();
        }

        // Delete event
        public async Task DeleteAsync(int id)
        {
            var ev = await _context.Events.FindAsync(id);
            if (ev != null)
            {
                _context.Events.Remove(ev);
                await _context.SaveChangesAsync();
            }
        }
        public async Task<bool> SubscribeAsync(int eventId, int userId)
        {
            var ev = await _context.Events.Include(e => e.Subscriptions)
                                          .FirstOrDefaultAsync(e => e.Id == eventId);

            if (ev == null) return false;

            // check max participants
            if (ev.Subscriptions.Count >= ev.MaxParticipants) return false;

            // check if already subscribed
            var already = await _context.Subscriptions
                .AnyAsync(s => s.EventId == eventId && s.UserId == userId);
            if (already) return false;

            // add subscription
            var sub = new Subscription
            {
                EventId = eventId,
                UserId = userId,
                DateSubscribed = DateTime.UtcNow
            };

            _context.Subscriptions.Add(sub);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}


