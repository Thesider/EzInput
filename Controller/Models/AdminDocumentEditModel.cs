using System.ComponentModel.DataAnnotations;

namespace Controller.Models;

public class AdminDocumentEditModel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string RawHtml { get; set; } = string.Empty;

    // Plain text preview derived from HTML - not posted back for content replacement unless RawHtml edited
    public string PlainTextPreview { get; set; } = string.Empty;
}
