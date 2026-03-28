using BussinessObject.Entities;
using DAO;
using Microsoft.EntityFrameworkCore;
using Repository.Interface;

namespace Repository.Implement;

public class FileTemplateRepository : IFileTemplateRepository
{
    private readonly EzInputDbContext _dbContext;

    public FileTemplateRepository(EzInputDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<FileTemplate>> GetByOwnerAsync(string ownerId)
    {
        return _dbContext.FileTemplates
            .Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync();
    }

    public Task<FileTemplate?> GetByIdForOwnerAsync(int id, string ownerId)
    {
        return _dbContext.FileTemplates
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
    }

    public async Task<FileTemplate> AddAsync(FileTemplate template)
    {
        _dbContext.FileTemplates.Add(template);
        await _dbContext.SaveChangesAsync();
        return template;
    }

    public async Task UpdateAsync(FileTemplate template)
    {
        _dbContext.FileTemplates.Update(template);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(FileTemplate template)
    {
        _dbContext.FileTemplates.Remove(template);
        await _dbContext.SaveChangesAsync();
    }
}
