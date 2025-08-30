using System;
using System.Collections.Generic;


namespace EventManagement.Models
{
    public class Event
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime StartDateTime { get; set; }
        public string? Location { get; set; }
        public int MaxParticipants { get; set; }
        public string? Category { get; set; }

        // navigation
        public List<Subscription> Subscriptions { get; set; } = new();
    }
}

