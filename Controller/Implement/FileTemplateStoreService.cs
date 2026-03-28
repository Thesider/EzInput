using Controller.Interface;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Controller.Implement;

public class FileTemplateStoreService : ITemplateStoreService
{
    private readonly IFileTemplateService _fileTemplateService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FileTemplateStoreService(IFileTemplateService fileTemplateService, IHttpContextAccessor httpContextAccessor)
    {
        _fileTemplateService = fileTemplateService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> SaveAsync(string? templateName, string templateHtml, CancellationToken cancellationToken = default)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            throw new InvalidOperationException("Authenticated user is required to store templates.");
        }

        var name = string.IsNullOrWhiteSpace(templateName) ? "template" : templateName.Trim();
        var template = await _fileTemplateService.CreateAsync(ownerId, name, templateHtml);
        return template.Id.ToString();
    }

    public async Task<IReadOnlyList<StoredTemplateItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Array.Empty<StoredTemplateItem>();
        }

        var templates = await _fileTemplateService.GetByOwnerAsync(ownerId);
        var items = templates
            .Select(template => new StoredTemplateItem(
                template.Id.ToString(),
                template.Name,
                template.UpdatedAtUtc))
            .ToList();

        return items;
    }

    public async Task<string?> GetHtmlAsync(string key, CancellationToken cancellationToken = default)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!int.TryParse(key, out var templateId))
        {
            return null;
        }

        var template = await _fileTemplateService.GetByIdForOwnerAsync(templateId, ownerId);
        return template?.HtmlContent;
    }

    private string? GetCurrentUserId()
    {
        return _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
