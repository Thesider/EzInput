using System.ComponentModel.DataAnnotations;

namespace EzInput.Models;

public class DocumentInputViewModel
{
    public int? Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;
}
