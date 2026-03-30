using System.Collections.Generic;

namespace Controller.Models;

public class AdminDocumentListViewModel
{
    public List<AdminDocumentRowModel> Documents { get; set; } = new List<AdminDocumentRowModel>();
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public string SortBy { get; set; } = "updated";
    public string SortDir { get; set; } = "desc";
    public string Query { get; set; } = string.Empty;
}
