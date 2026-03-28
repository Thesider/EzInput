using BussinessObject.Entities;

namespace Controller.Interface;

public interface IFileTemplateService
{
    Task<List<FileTemplate>> GetByOwnerAsync(string ownerId);
    Task<FileTemplate?> GetByIdForOwnerAsync(int id, string ownerId);
    Task<FileTemplate> CreateAsync(string ownerId, string name, string htmlContent);
    Task<bool> UpdateAsync(int id, string ownerId, string name, string htmlContent);
    Task<bool> DeleteAsync(int id, string ownerId);
}
