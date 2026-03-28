using System.ComponentModel.DataAnnotations;

namespace BussinessObject.Entities;

public class FileTemplate
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string HtmlContent { get; set; } = string.Empty;

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    public User? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
