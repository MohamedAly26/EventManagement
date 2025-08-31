using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventManagement.Models
{
    public class Event
    {
        public int Id { get; set; }
        [Required] public string Title { get; set; } = "";
        public string? Description { get; set; }
        public DateTime StartDateTime { get; set; }
        public string Location { get; set; } = "";
        public int MaxParticipants { get; set; } = 0;
        public string? Category { get; set; }

        // navigation
        public List<Subscription> Subscriptions { get; set; } = new();
    }
}

