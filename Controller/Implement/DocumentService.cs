using BussinessObject.Entities;
using Controller.Interface;
using Repository.Interface;

namespace Controller.Implement;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IHtmlSanitizationService _htmlSanitizationService;

    public DocumentService(
        IDocumentRepository documentRepository,
        IHtmlSanitizationService htmlSanitizationService)
    {
        _documentRepository = documentRepository;
        _htmlSanitizationService = htmlSanitizationService;
    }

    public Task<List<Document>> GetByOwnerAsync(string ownerId)
    {
        return _documentRepository.GetByOwnerAsync(ownerId);
    }

    public Task<Document?> GetByIdForOwnerAsync(int id, string ownerId)
    {
        return _documentRepository.GetByIdForOwnerAsync(id, ownerId);
    }

    public Task<Document> CreateAsync(string ownerId, string title, string content)
    {
        var sanitizedTitle = _htmlSanitizationService.SanitizePlainText(title);
        var sanitizedContent = _htmlSanitizationService.SanitizeRichText(content);
        var now = DateTime.UtcNow;
        var document = new Document
        {
            OwnerId = ownerId,
            Title = sanitizedTitle,
            Content = sanitizedContent,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        return _documentRepository.AddAsync(document);
    }

    public async Task<bool> UpdateAsync(int id, string ownerId, string title, string content)
    {
        var existing = await _documentRepository.GetByIdForOwnerAsync(id, ownerId);
        if (existing is null)
        {
            return false;
        }

        existing.Title = _htmlSanitizationService.SanitizePlainText(title);
        existing.Content = _htmlSanitizationService.SanitizeRichText(content);
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _documentRepository.UpdateAsync(existing);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, string ownerId)
    {
        var existing = await _documentRepository.GetByIdForOwnerAsync(id, ownerId);
        if (existing is null)
        {
            return false;
        }

        await _documentRepository.DeleteAsync(existing);
        return true;
    }
}
