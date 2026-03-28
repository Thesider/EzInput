using BussinessObject.Entities;
using Controller.Interface;
using Repository.Interface;

namespace Controller.Implement;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public Task<List<User>> GetAllAsync()
    {
        return _userRepository.GetAllAsync();
    }

    public Task<User?> GetByIdAsync(string id)
    {
        return _userRepository.GetByIdAsync(id);
    }

    public Task<User?> GetByEmailAsync(string email)
    {
        return _userRepository.GetByEmailAsync(email);
    }

    public Task<User> CreateAsync(string email, string userName)
    {
        var normalizedEmail = email.Trim();

        var user = new User
        {
            Email = normalizedEmail,
            NormalizedEmail = normalizedEmail.ToUpperInvariant(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            EmailConfirmed = false,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
            AccessFailedCount = 0
        };

        return _userRepository.AddAsync(user);
    }

    public async Task<bool> UpdateAsync(string id, string email, string userName)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user is null)
        {
            return false;
        }

        var normalizedEmail = email.Trim();

        user.Email = normalizedEmail;
        user.NormalizedEmail = normalizedEmail.ToUpperInvariant();
        user.UserName = userName;
        user.NormalizedUserName = userName.ToUpperInvariant();
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        await _userRepository.UpdateAsync(user);
        return true;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user is null)
        {
            return false;
        }

        await _userRepository.DeleteAsync(user);
        return true;
    }
}
