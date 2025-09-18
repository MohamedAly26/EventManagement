using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventManagement.Models;
public class Comment
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int? ParentId { get; set; }
    public string Body { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public bool FromAdmin { get; set; }
    public bool IsHidden { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<Comment> Replies { get; set; } = new();
}


