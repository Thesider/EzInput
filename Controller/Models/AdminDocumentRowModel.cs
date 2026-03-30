using System;

namespace Controller.Models;

public class AdminDocumentRowModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
