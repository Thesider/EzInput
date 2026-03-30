using Controller.Models;
using System.Threading.Tasks;

namespace Controller.Interface;

public interface IAdminService
{
    Task<AdminDashboardViewModel> GetDashboardAsync();
}
