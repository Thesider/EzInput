using System.Collections.Generic;

namespace Controller.Models;

public class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int TotalDocuments { get; set; }
    public int TotalTemplates { get; set; }
    public List<string> RecentUsers { get; set; } = new List<string>();
}
