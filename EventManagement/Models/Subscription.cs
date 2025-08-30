using System;


namespace EventManagement.Models
{
    public class Subscription
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public int UserId { get; set; }

        public DateTime DateSubscribed { get; set; }

        // navigation
        public Event? Event { get; set; }
        public User? User { get; set; }
    }
}
