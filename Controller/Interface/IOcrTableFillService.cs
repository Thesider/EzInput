using Controller.Models;

namespace Controller.Interface;

public interface IOcrTableFillService
{
    Task<OcrTableFillResult> FillAsync(string templateHtml, string ocrText, CancellationToken cancellationToken = default);
}
