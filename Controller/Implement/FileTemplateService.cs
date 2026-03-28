using BussinessObject.Entities;
using Controller.Interface;
using Repository.Interface;

namespace Controller.Implement;

public class FileTemplateService : IFileTemplateService
{
    private readonly IFileTemplateRepository _fileTemplateRepository;
    private readonly IHtmlSanitizationService _htmlSanitizationService;

    public FileTemplateService(
        IFileTemplateRepository fileTemplateRepository,
        IHtmlSanitizationService htmlSanitizationService)
    {
        _fileTemplateRepository = fileTemplateRepository;
        _htmlSanitizationService = htmlSanitizationService;
    }

    public Task<List<FileTemplate>> GetByOwnerAsync(string ownerId)
    {
        return _fileTemplateRepository.GetByOwnerAsync(ownerId);
    }

    public Task<FileTemplate?> GetByIdForOwnerAsync(int id, string ownerId)
    {
        return _fileTemplateRepository.GetByIdForOwnerAsync(id, ownerId);
    }

    public Task<FileTemplate> CreateAsync(string ownerId, string name, string htmlContent)
    {
        var now = DateTime.UtcNow;

        var template = new FileTemplate
        {
            OwnerId = ownerId,
            Name = _htmlSanitizationService.SanitizePlainText(name),
            HtmlContent = _htmlSanitizationService.SanitizeRichText(htmlContent),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        return _fileTemplateRepository.AddAsync(template);
    }

    public async Task<bool> UpdateAsync(int id, string ownerId, string name, string htmlContent)
    {
        var existing = await _fileTemplateRepository.GetByIdForOwnerAsync(id, ownerId);
        if (existing is null)
        {
            return false;
        }

        existing.Name = _htmlSanitizationService.SanitizePlainText(name);
        existing.HtmlContent = _htmlSanitizationService.SanitizeRichText(htmlContent);
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _fileTemplateRepository.UpdateAsync(existing);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, string ownerId)
    {
        var existing = await _fileTemplateRepository.GetByIdForOwnerAsync(id, ownerId);
        if (existing is null)
        {
            return false;
        }

        await _fileTemplateRepository.DeleteAsync(existing);
        return true;
    }
}
