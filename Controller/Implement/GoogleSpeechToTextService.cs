using Controller.Interface;
using Controller.Models;
using Google.Cloud.Speech.V1;
using Microsoft.Extensions.Configuration;

namespace Controller.Implement;

public class GoogleSpeechToTextService : ISpeechToTextService
{
    private readonly string _primaryLanguage;
    private readonly string _secondaryLanguage;

    public GoogleSpeechToTextService(IConfiguration configuration)
    {
        _primaryLanguage = configuration["AiPipeline:SpeechToText:PrimaryLanguage"] ?? "vi-VN";
        _secondaryLanguage = configuration["AiPipeline:SpeechToText:SecondaryLanguage"] ?? "en-US";
    }

    public async Task<AiPipelineResult> TranscribeAsync(Stream audioStream, string? contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            if (audioStream.Length == 0)
            {
                return AiPipelineResult.Fail("Audio file is empty.", "Google Speech-to-Text");
            }

            await using var memory = new MemoryStream();
            await audioStream.CopyToAsync(memory, cancellationToken);

            var client = await SpeechClient.CreateAsync();

            var encoding = ResolveEncoding(contentType);
            var config = new RecognitionConfig
            {
                Encoding = encoding,
                LanguageCode = _primaryLanguage,
                AlternativeLanguageCodes = { _secondaryLanguage },
                EnableAutomaticPunctuation = true,
                Model = "latest_short"
            };

            var response = await client.RecognizeAsync(new RecognizeRequest
            {
                Config = config,
                Audio = RecognitionAudio.FromBytes(memory.ToArray())
            }, cancellationToken: cancellationToken);

            var text = string.Join(" ", response.Results
                .Select(r => r.Alternatives.FirstOrDefault()?.Transcript)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

            text = text.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return AiPipelineResult.Fail("No speech could be transcribed from the audio.", "Google Speech-to-Text");
            }

            return AiPipelineResult.Ok(text, "Google Speech-to-Text");
        }
        catch (Exception ex)
        {
            return AiPipelineResult.Fail($"Speech-to-text failed: {ex.Message}", "Google Speech-to-Text");
        }
    }

    private static RecognitionConfig.Types.AudioEncoding ResolveEncoding(string? contentType)
    {
        var normalized = (contentType ?? string.Empty).ToLowerInvariant();

        if (normalized.Contains("webm"))
        {
            return RecognitionConfig.Types.AudioEncoding.WebmOpus;
        }

        if (normalized.Contains("ogg"))
        {
            return RecognitionConfig.Types.AudioEncoding.OggOpus;
        }

        if (normalized.Contains("mpeg") || normalized.Contains("mp3"))
        {
            return RecognitionConfig.Types.AudioEncoding.Mp3;
        }

        return RecognitionConfig.Types.AudioEncoding.Linear16;
    }
}
