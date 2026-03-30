using Controller.Interface;
using EzInput.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EzInput.Controllers;

[Authorize]
[Route("[controller]")]
public class PipelineController : Microsoft.AspNetCore.Mvc.Controller
{
    private readonly IOcrService _ocrService;
    private readonly IOcrTableFillService _ocrTableFillService;
    private readonly ISpeechToTextService _speechToTextService;
    private readonly ITemplateStoreService _templateStoreService;

    public PipelineController(
        IOcrService ocrService,
        IOcrTableFillService ocrTableFillService,
        ISpeechToTextService speechToTextService,
        ITemplateStoreService templateStoreService)
    {
        _ocrService = ocrService;
        _ocrTableFillService = ocrTableFillService;
        _speechToTextService = speechToTextService;
        _templateStoreService = templateStoreService;
    }

    [HttpGet("templates")]
    public async Task<IActionResult> Templates(CancellationToken cancellationToken)
    {
        var items = await _templateStoreService.ListAsync(cancellationToken);

        var templates = new List<object>(items.Count);
        foreach (var item in items)
        {
            var html = await _templateStoreService.GetHtmlAsync(item.Key, cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                continue;
            }

            templates.Add(new
            {
                key = item.Key,
                name = item.DisplayName,
                lastModifiedUtc = item.LastModifiedUtc,
                html
            });
        }

        return Ok(new
        {
            success = true,
            templates
        });
    }

    [HttpPost("ocr")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    public async Task<IActionResult> Ocr(IFormFile? imageFile, CancellationToken cancellationToken)
    {
        if (imageFile is null || imageFile.Length == 0)
        {
            return BadRequest(new AiPipelineResponseViewModel
            {
                Success = false,
                Provider = "Tesseract",
                ErrorMessage = "Please upload an image file."
            });
        }

        await using var stream = imageFile.OpenReadStream();
        var result = await _ocrService.ExtractTextAsync(stream, imageFile.FileName, cancellationToken);

        var response = new AiPipelineResponseViewModel
        {
            Success = result.Success,
            Text = result.Text,
            Provider = result.Provider,
            ErrorMessage = result.ErrorMessage
        };

        return result.Success ? Ok(response) : BadRequest(response);
    }

    [HttpPost("stt")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 25 * 1024 * 1024)]
    public async Task<IActionResult> SpeechToText(IFormFile? audioFile, CancellationToken cancellationToken)
    {
        if (audioFile is null || audioFile.Length == 0)
        {
            return BadRequest(new AiPipelineResponseViewModel
            {
                Success = false,
                Provider = "Whisper (Local)",
                ErrorMessage = "Please upload an audio file."
            });
        }

        await using var stream = audioFile.OpenReadStream();
        var result = await _speechToTextService.TranscribeAsync(stream, audioFile.ContentType, cancellationToken);

        var response = new AiPipelineResponseViewModel
        {
            Success = result.Success,
            Text = result.Text,
            Provider = result.Provider,
            ErrorMessage = result.ErrorMessage
        };

        return result.Success ? Ok(response) : BadRequest(response);
    }

    [HttpPost("ocr-fill-table")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    public async Task<IActionResult> OcrFillTable(
        IFormFile? imageFile,
        string? templateHtml,
        string? templateName,
        string? rawOcrText,
        string? heuristicMode,
        bool saveTemplate = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateHtml))
        {
            return BadRequest(new AiPipelineResponseViewModel
            {
                Success = false,
                Provider = "Tesseract",
                ErrorMessage = "Template HTML is required."
            });
        }

        var extractedText = rawOcrText;
        var provider = "Tesseract";

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            if (imageFile is null || imageFile.Length == 0)
            {
                return BadRequest(new AiPipelineResponseViewModel
                {
                    Success = false,
                    Provider = provider,
                    ErrorMessage = "Please upload an image file or provide raw OCR text."
                });
            }

            await using var imageStream = imageFile.OpenReadStream();
            var ocrResult = await _ocrService.ExtractTextAsync(imageStream, imageFile.FileName, cancellationToken);
            if (!ocrResult.Success)
            {
                return BadRequest(new AiPipelineResponseViewModel
                {
                    Success = false,
                    Provider = ocrResult.Provider,
                    ErrorMessage = ocrResult.ErrorMessage
                });
            }

            extractedText = ocrResult.Text;
            provider = ocrResult.Provider;
        }

        var heuristic = string.IsNullOrWhiteSpace(heuristicMode) ? "auto" : heuristicMode;
        var fillResult = await _ocrTableFillService.FillAsync(templateHtml, extractedText ?? string.Empty, heuristic, cancellationToken);

        var templateSaved = false;
        string? savedTemplatePath = null;

        if (saveTemplate)
        {
            savedTemplatePath = await _templateStoreService.SaveAsync(templateName, templateHtml, cancellationToken);
            templateSaved = true;
        }

        var response = new AiPipelineResponseViewModel
        {
            Success = fillResult.Success,
            Provider = provider,
            Text = fillResult.ExtractedText,
            FilledHtml = fillResult.FilledHtml,
            ErrorMessage = fillResult.ErrorMessage,
            MatchedFields = fillResult.MatchedFields,
            MissingFields = fillResult.MissingFields,
            MatchedCount = fillResult.MatchedCount,
            RequiredCount = fillResult.RequiredCount,
            CanAutoFill = fillResult.CanAutoFill,
            UsedStructuredFallback = fillResult.UsedStructuredFallback,
            RejectedLines = fillResult.RejectedLines,
            TemplateSaved = templateSaved,
            SavedTemplatePath = savedTemplatePath
        };

        return fillResult.Success ? Ok(response) : BadRequest(response);
    }
}
