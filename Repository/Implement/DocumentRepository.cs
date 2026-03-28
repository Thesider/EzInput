using BussinessObject.Entities;
using DAO;
using Microsoft.EntityFrameworkCore;
using Repository.Interface;

namespace Repository.Implement;

public class DocumentRepository : IDocumentRepository
{
    private readonly EzInputDbContext _dbContext;

    public DocumentRepository(EzInputDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Document>> GetByOwnerAsync(string ownerId)
    {
        return _dbContext.Documents
            .Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync();
    }

    public Task<Document?> GetByIdForOwnerAsync(int id, string ownerId)
    {
        return _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
    }

    public async Task<Document> AddAsync(Document document)
    {
        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync();
        return document;
    }

    public async Task UpdateAsync(Document document)
    {
        _dbContext.Documents.Update(document);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(Document document)
    {
        _dbContext.Documents.Remove(document);
        await _dbContext.SaveChangesAsync();
    }
}
