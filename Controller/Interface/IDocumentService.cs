using BussinessObject.Entities;

namespace Controller.Interface;

public interface IDocumentService
{
    Task<List<Document>> GetByOwnerAsync(string ownerId);
    Task<Document?> GetByIdForOwnerAsync(int id, string ownerId);
    Task<Document> CreateAsync(string ownerId, string title, string content);
    Task<bool> UpdateAsync(int id, string ownerId, string title, string content);
    Task<bool> DeleteAsync(int id, string ownerId);
}
