using System.Text;
using Controller.Interface;
using Controller.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Whisper.net;
using Whisper.net.Ggml;

namespace Controller.Implement;

public class WhisperSpeechToTextService : ISpeechToTextService
{
    private readonly string _primaryLanguage;
    private readonly GgmlType _modelType;
    private readonly string _modelPath;
    private readonly bool _autoDownloadModel;
    private readonly SemaphoreSlim _factoryLock = new(1, 1);
    private WhisperFactory? _whisperFactory;

    public WhisperSpeechToTextService(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        _primaryLanguage = configuration["AiPipeline:SpeechToText:PrimaryLanguage"] ?? "auto";
        _modelType = ParseModelType(configuration["AiPipeline:SpeechToText:Model"]);
        _autoDownloadModel = bool.TryParse(configuration["AiPipeline:SpeechToText:AutoDownloadModel"], out var autoDownload)
            ? autoDownload
            : true;

        var configuredModelPath = configuration["AiPipeline:SpeechToText:ModelPath"] ?? "WhisperModels/ggml-base.bin";
        _modelPath = Path.IsPathRooted(configuredModelPath)
            ? configuredModelPath
            : Path.Combine(hostEnvironment.ContentRootPath, configuredModelPath);
    }

    public async Task<AiPipelineResult> TranscribeAsync(Stream audioStream, string? contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            if (audioStream.Length == 0)
            {
                return AiPipelineResult.Fail("Audio file is empty.", "Whisper (Local)");
            }

            await using var memory = new MemoryStream();
            await audioStream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            if (!LooksLikeWav(memory, contentType))
            {
                return AiPipelineResult.Fail("Whisper local mode currently accepts WAV audio. Please use the Record Voice button or upload a .wav file.", "Whisper (Local)");
            }

            memory.Position = 0;
            var factory = await GetFactoryAsync(cancellationToken);
            var builder = factory.CreateBuilder();

            var normalizedLanguage = NormalizeLanguage(_primaryLanguage);
            if (!string.IsNullOrWhiteSpace(normalizedLanguage))
            {
                builder.WithLanguage(normalizedLanguage);
            }

            using var processor = builder.Build();

            var textBuilder = new StringBuilder();
            await foreach (var result in processor.ProcessAsync(memory))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    if (textBuilder.Length > 0)
                    {
                        textBuilder.Append(' ');
                    }

                    textBuilder.Append(result.Text.Trim());
                }
            }

            var text = textBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return AiPipelineResult.Fail("No speech could be transcribed from the audio.", "Whisper (Local)");
            }

            return AiPipelineResult.Ok(text, "Whisper (Local)");
        }
        catch (FileNotFoundException)
        {
            return AiPipelineResult.Fail($"Whisper model file was not found at '{_modelPath}'.", "Whisper (Local)");
        }
        catch (Exception ex)
        {
            return AiPipelineResult.Fail($"Speech-to-text failed: {ex.Message}", "Whisper (Local)");
        }
    }

    private async Task<WhisperFactory> GetFactoryAsync(CancellationToken cancellationToken)
    {
        if (_whisperFactory is not null)
        {
            return _whisperFactory;
        }

        await _factoryLock.WaitAsync(cancellationToken);
        try
        {
            if (_whisperFactory is not null)
            {
                return _whisperFactory;
            }

            await EnsureModelExistsAsync(cancellationToken);
            _whisperFactory = WhisperFactory.FromPath(_modelPath);
            return _whisperFactory;
        }
        finally
        {
            _factoryLock.Release();
        }
    }

    private async Task EnsureModelExistsAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_modelPath))
        {
            return;
        }

        if (!_autoDownloadModel)
        {
            throw new FileNotFoundException("Whisper model not found and auto download is disabled.", _modelPath);
        }

        var modelDirectory = Path.GetDirectoryName(_modelPath);
        if (!string.IsNullOrWhiteSpace(modelDirectory))
        {
            Directory.CreateDirectory(modelDirectory);
        }

        using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(_modelType);
        await using var fileWriter = File.OpenWrite(_modelPath);
        await modelStream.CopyToAsync(fileWriter, cancellationToken);
    }

    private static bool LooksLikeWav(Stream stream, string? contentType)
    {
        var normalized = (contentType ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("wav") || normalized.Contains("wave"))
        {
            return true;
        }

        if (!stream.CanSeek || stream.Length < 12)
        {
            return false;
        }

        var originalPosition = stream.Position;
        try
        {
            var header = new byte[12];
            _ = stream.Read(header, 0, header.Length);
            var riff = header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F';
            var wave = header[8] == (byte)'W' && header[9] == (byte)'A' && header[10] == (byte)'V' && header[11] == (byte)'E';
            return riff && wave;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "auto";
        }

        var normalized = language.Trim().ToLowerInvariant();
        if (normalized == "auto")
        {
            return "auto";
        }

        var separatorIndex = normalized.IndexOf('-');
        return separatorIndex > 0 ? normalized[..separatorIndex] : normalized;
    }

    private static GgmlType ParseModelType(string? model)
    {
        return (model ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "tiny" => GgmlType.Tiny,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            _ => GgmlType.Base
        };
    }
}
