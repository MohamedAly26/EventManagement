using Microsoft.AspNetCore.Identity;

namespace EventManagement.Models;

public class Subscription
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    public string UserId { get; set; } = default!; // <-- string
    public IdentityUser? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
