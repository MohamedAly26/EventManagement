using System.Collections.Generic;


namespace EventManagement.Models
{
    public class User
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        public string Email { get; set; } = null!;
        public string? FullName { get; set; }

        // navigation
        public List<Subscription> Subscriptions { get; set; } = new();
    }
}
