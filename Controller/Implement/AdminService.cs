using Controller.Interface;
using Controller.Models;
using DAO;
using Microsoft.EntityFrameworkCore;

namespace Controller.Implement;

public class AdminService : IAdminService
{
    private readonly EzInputDbContext _dbContext;

    public AdminService(EzInputDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminDashboardViewModel> GetDashboardAsync()
    {
        var usersCount = await _dbContext.Users.CountAsync();
        var docsCount = await _dbContext.Documents.CountAsync();
        var templatesCount = await _dbContext.FileTemplates.CountAsync();

        var recentUsers = await _dbContext.Users
            .OrderBy(u => u.Email)
            .Select(u => u.Email)
            .Take(5)
            .ToListAsync();

        return new AdminDashboardViewModel
        {
            TotalUsers = usersCount,
            TotalDocuments = docsCount,
            TotalTemplates = templatesCount,
            RecentUsers = recentUsers
        };
    }
}
