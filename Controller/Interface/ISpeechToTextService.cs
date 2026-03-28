using Controller.Models;

namespace Controller.Interface;

public interface ISpeechToTextService
{
    Task<AiPipelineResult> TranscribeAsync(Stream audioStream, string? contentType, CancellationToken cancellationToken = default);
}
