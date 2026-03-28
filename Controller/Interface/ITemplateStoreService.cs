namespace Controller.Interface;

public record StoredTemplateItem(string Key, string DisplayName, DateTime LastModifiedUtc);

public interface ITemplateStoreService
{
    Task<string> SaveAsync(string? templateName, string templateHtml, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredTemplateItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<string?> GetHtmlAsync(string key, CancellationToken cancellationToken = default);
}
