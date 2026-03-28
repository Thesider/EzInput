using Controller.Interface;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Controller.Implement;

public class FileTemplateStoreService : ITemplateStoreService
{
    private readonly string _storageFolder;

    public FileTemplateStoreService(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredFolder = configuration["AiPipeline:TemplateFill:StorageFolder"] ?? "TemplateStore";
        _storageFolder = Path.IsPathRooted(configuredFolder)
            ? configuredFolder
            : Path.Combine(environment.ContentRootPath, configuredFolder);
    }

    public async Task<string> SaveAsync(string? templateName, string templateHtml, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_storageFolder);

        var normalizedTemplateName = string.IsNullOrWhiteSpace(templateName)
            ? "template"
            : string.Concat(templateName.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'));

        if (string.IsNullOrWhiteSpace(normalizedTemplateName))
        {
            normalizedTemplateName = "template";
        }

        var fileName = $"{normalizedTemplateName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.html";
        var fullPath = Path.Combine(_storageFolder, fileName);

        await File.WriteAllTextAsync(fullPath, templateHtml, cancellationToken);

        return fullPath;
    }

    public Task<IReadOnlyList<StoredTemplateItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_storageFolder))
        {
            return Task.FromResult<IReadOnlyList<StoredTemplateItem>>(Array.Empty<StoredTemplateItem>());
        }

        var items = Directory.EnumerateFiles(_storageFolder, "*.html", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => new StoredTemplateItem(
                info.Name,
                Path.GetFileNameWithoutExtension(info.Name),
                info.LastWriteTimeUtc))
            .ToList();

        return Task.FromResult<IReadOnlyList<StoredTemplateItem>>(items);
    }

    public async Task<string?> GetHtmlAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (key.IndexOfAny(invalidChars) >= 0 || key.Contains("..") || key.Contains('/') || key.Contains('\\'))
        {
            return null;
        }

        var fullPath = Path.Combine(_storageFolder, key);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }
}
