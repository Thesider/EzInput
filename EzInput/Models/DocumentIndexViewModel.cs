using BussinessObject.Entities;

namespace EzInput.Models;

public class DocumentIndexViewModel
{
    public IReadOnlyCollection<Document> Documents { get; init; } = Array.Empty<Document>();

    public string Query { get; init; } = string.Empty;

    public int CurrentPage { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages => TotalItems == 0 ? 1 : (int)Math.Ceiling((double)TotalItems / PageSize);

    public int StartItemIndex => TotalItems == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;

    public int EndItemIndex => Math.Min(CurrentPage * PageSize, TotalItems);
}
