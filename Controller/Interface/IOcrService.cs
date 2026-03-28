using Controller.Models;

namespace Controller.Interface;

public interface IOcrService
{
    Task<AiPipelineResult> ExtractTextAsync(Stream imageStream, string? fileName, CancellationToken cancellationToken = default);
}
