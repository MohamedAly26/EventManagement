using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventManagement.Models
{
    public class Event
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Title { get; set; } = "";
        public string? Description { get; set; }

        [Required]
        public DateTime StartDateTime { get; set; } = DateTime.Now;

        [Required, StringLength(200)]
        public string Location { get; set; } = "";

        public string? Category { get; set; }

        [Range(1, 100000)]
        public int MaxParticipants { get; set; }

       
      

        public List<Subscription>? Subscriptions { get; set; }
    }
}

