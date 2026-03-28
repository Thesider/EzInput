using BussinessObject.Entities;
using DAO;
using Microsoft.EntityFrameworkCore;
using Repository.Interface;

namespace Repository.Implement;

public class UserRepository : IUserRepository
{
    private readonly EzInputDbContext _dbContext;

    public UserRepository(EzInputDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<User>> GetAllAsync()
    {
        return _dbContext.Users
            .OrderBy(x => x.Email)
            .ToListAsync();
    }

    public Task<User?> GetByIdAsync(string id)
    {
        return _dbContext.Users.FirstOrDefaultAsync(x => x.Id == id);
    }

    public Task<User?> GetByEmailAsync(string email)
    {
        return _dbContext.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
    }

    public async Task<User> AddAsync(User user)
    {
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(User user)
    {
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();
    }
}
