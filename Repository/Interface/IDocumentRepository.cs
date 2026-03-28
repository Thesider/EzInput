using BussinessObject.Entities;

namespace Repository.Interface;

public interface IDocumentRepository
{
    Task<List<Document>> GetByOwnerAsync(string ownerId);
    Task<Document?> GetByIdForOwnerAsync(int id, string ownerId);
    Task<Document> AddAsync(Document document);
    Task UpdateAsync(Document document);
    Task DeleteAsync(Document document);
}
