using BussinessObject.Entities;

namespace Repository.Interface;

public interface IFileTemplateRepository
{
    Task<List<FileTemplate>> GetByOwnerAsync(string ownerId);
    Task<FileTemplate?> GetByIdForOwnerAsync(int id, string ownerId);
    Task<FileTemplate> AddAsync(FileTemplate template);
    Task UpdateAsync(FileTemplate template);
    Task DeleteAsync(FileTemplate template);
}
