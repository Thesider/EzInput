namespace EzInput.Models;

public class AiPipelineResponseViewModel
{
    public bool Success { get; init; }

    public string Text { get; init; } = string.Empty;

    public string FilledHtml { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public Dictionary<string, string> MatchedFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> MissingFields { get; init; } = [];

    public int MatchedCount { get; init; }

    public int RequiredCount { get; init; }

    public bool CanAutoFill { get; init; }

    public bool UsedStructuredFallback { get; init; }

    public List<string> RejectedLines { get; init; } = [];

    public bool TemplateSaved { get; init; }

    public string? SavedTemplatePath { get; init; }
}
