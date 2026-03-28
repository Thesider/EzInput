namespace Controller.Models;

public class AiPipelineResult
{
    public bool Success { get; init; }

    public string Text { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public static AiPipelineResult Ok(string text, string provider)
    {
        return new AiPipelineResult
        {
            Success = true,
            Text = text,
            Provider = provider
        };
    }

    public static AiPipelineResult Fail(string errorMessage, string provider)
    {
        return new AiPipelineResult
        {
            Success = false,
            Provider = provider,
            ErrorMessage = errorMessage
        };
    }
}
