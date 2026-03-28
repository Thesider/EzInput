using BussinessObject.Entities;

namespace Controller.Interface;

public interface IUserService
{
    Task<List<User>> GetAllAsync();
    Task<User?> GetByIdAsync(string id);
    Task<User?> GetByEmailAsync(string email);
    Task<User> CreateAsync(string email, string userName);
    Task<bool> UpdateAsync(string id, string email, string userName);
    Task<bool> DeleteAsync(string id);
}
