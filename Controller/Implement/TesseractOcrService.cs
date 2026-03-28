using Controller.Interface;
using Controller.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tesseract;

namespace Controller.Implement;

public class TesseractOcrService : IOcrService
{
    private readonly string _tessDataPath;
    private readonly string _language;

    public TesseractOcrService(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredPath = configuration["AiPipeline:Ocr:TessdataPath"];
        var resolvedPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "tessdata")
            : configuredPath;

        _tessDataPath = Path.IsPathRooted(resolvedPath)
            ? resolvedPath
            : Path.Combine(environment.ContentRootPath, resolvedPath);

        _language = configuration["AiPipeline:Ocr:Language"] ?? "eng+vie";
    }

    public async Task<AiPipelineResult> ExtractTextAsync(Stream imageStream, string? fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (imageStream.Length == 0)
            {
                return AiPipelineResult.Fail("Image file is empty.", "Tesseract");
            }

            await using var memory = new MemoryStream();
            await imageStream.CopyToAsync(memory, cancellationToken);
            var imageBytes = memory.ToArray();

            using var engine = new TesseractEngine(_tessDataPath, _language, EngineMode.Default);
            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(pix);
            var text = (page.GetText() ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return AiPipelineResult.Fail("No text could be detected in the image.", "Tesseract");
            }

            return AiPipelineResult.Ok(text, "Tesseract");
        }
        catch (Exception ex)
        {
            return AiPipelineResult.Fail($"OCR failed: {ex.Message}", "Tesseract");
        }
    }
}
