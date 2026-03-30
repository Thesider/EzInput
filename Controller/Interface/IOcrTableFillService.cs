using Controller.Models;

namespace Controller.Interface;

public interface IOcrTableFillService
{
    // heuristicMode: "auto" | "schema" | "labelValue"
    Task<OcrTableFillResult> FillAsync(string templateHtml, string ocrText, string heuristicMode = "auto", CancellationToken cancellationToken = default);
}
