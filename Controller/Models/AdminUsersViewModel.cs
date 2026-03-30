using System.Collections.Generic;

namespace Controller.Models;

public class AdminUsersViewModel
{
    public List<AdminUserRowModel> Users { get; set; } = new List<AdminUserRowModel>();
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public string SortBy { get; set; } = "email";
    public string SortDir { get; set; } = "asc";
    public string Query { get; set; } = string.Empty;
}
