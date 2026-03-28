namespace Controller.Models;

public class OcrTableFillResult
{
    public bool Success { get; init; }

    public string Provider { get; init; } = "Tesseract";

    public string ExtractedText { get; init; } = string.Empty;

    public string FilledHtml { get; init; } = string.Empty;

    public Dictionary<string, string> MatchedFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> MissingFields { get; init; } = [];

    public int MatchedCount { get; init; }

    public int RequiredCount { get; init; }

    public bool CanAutoFill { get; init; }

    public bool UsedStructuredFallback { get; init; }

    public List<string> RejectedLines { get; init; } = [];

    public bool TemplateSaved { get; init; }

    public string? SavedTemplatePath { get; init; }

    public string? ErrorMessage { get; init; }

    public static OcrTableFillResult Fail(string errorMessage)
    {
        return new OcrTableFillResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
