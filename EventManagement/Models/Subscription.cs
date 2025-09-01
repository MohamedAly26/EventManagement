using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventManagement.Models;

public class Subscription
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!; // <-- string
    public IdentityUser? User { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }


    public DateTime DataSubscription { get; set; } = DateTime.UtcNow;
}
