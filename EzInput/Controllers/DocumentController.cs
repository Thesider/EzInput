using System.Security.Claims;
using System.Net;
using System.Text.RegularExpressions;
using Controller.Interface;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using EzInput.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EzInput.Controllers;

[Authorize]
public class DocumentController : Microsoft.AspNetCore.Mvc.Controller
{
    private readonly IDocumentService _documentService;

    public DocumentController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? query, int page = 1, int pageSize = 10)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Challenge();
        }

        var documents = await _documentService.GetByOwnerAsync(ownerId);

        var normalizedQuery = (query ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            documents = documents
                .Where(x => x.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        pageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 50);
        var totalItems = documents.Count;
        var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling((double)totalItems / pageSize);
        page = page < 1 ? 1 : Math.Min(page, totalPages);

        var pageItems = documents
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var model = new DocumentIndexViewModel
        {
            Documents = pageItems,
            Query = normalizedQuery,
            CurrentPage = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };

        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new DocumentInputViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DocumentInputViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Challenge();
        }

        await _documentService.CreateAsync(ownerId, model.Title, model.Content);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Challenge();
        }

        var document = await _documentService.GetByIdForOwnerAsync(id, ownerId);
        if (document is null)
        {
            return NotFound();
        }

        var model = new DocumentInputViewModel
        {
            Id = document.Id,
            Title = document.Title,
            Content = document.Content
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DocumentInputViewModel model)
    {
        if (!ModelState.IsValid || model.Id is null)
        {
            return View(model);
        }

        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Challenge();
        }

        var updated = await _documentService.UpdateAsync(model.Id.Value, ownerId, model.Title, model.Content);
        if (!updated)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Challenge();
        }

        await _documentService.DeleteAsync(id, ownerId);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ExportDoc(int id)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Challenge();
        }

        var document = await _documentService.GetByIdForOwnerAsync(id, ownerId);
        if (document is null)
        {
            return NotFound();
        }

        var bytes = BuildDocxBytes(document.Title, document.Content);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"{BuildSafeFileName(document.Title)}.docx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(int id)
    {
        var ownerId = GetCurrentUserId();
        if (ownerId is null)
        {
            return Challenge();
        }

        var document = await _documentService.GetByIdForOwnerAsync(id, ownerId);
        if (document is null)
        {
            return NotFound();
        }

        var bytes = BuildXlsxBytes(document.Title, document.Content);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{BuildSafeFileName(document.Title)}.xlsx");
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private static string BuildSafeFileName(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(title
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }

    private static byte[] BuildDocxBytes(string title, string htmlContent)
    {
        using var stream = new MemoryStream();
        using (var wordDocument = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            body.Append(CreateTitleParagraph(title));

            var rows = ExtractTableRowsFromHtml(htmlContent);
            if (rows.Count > 0)
            {
                body.Append(CreateWordTable(rows));
            }
            else
            {
                foreach (var line in ExtractTextLines(htmlContent))
                {
                    body.Append(new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(line) { Space = SpaceProcessingModeValues.Preserve })));
                }
            }
        }

        return stream.ToArray();
    }

    private static byte[] BuildXlsxBytes(string title, string htmlContent)
    {
        using var stream = new MemoryStream();
        using (var spreadsheet = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = spreadsheet.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Document"
            });

            var tableRows = ExtractTableRowsFromHtml(htmlContent);
            if (tableRows.Count > 0)
            {
                uint rowIndex = 1;
                foreach (var rowValues in tableRows)
                {
                    var row = new Row { RowIndex = rowIndex++ };
                    foreach (var value in rowValues)
                    {
                        row.Append(CreateInlineStringCell(value));
                    }

                    sheetData.Append(row);
                }
            }
            else
            {
                var titleRow = new Row { RowIndex = 1 };
                titleRow.Append(CreateInlineStringCell(title));
                sheetData.Append(titleRow);

                uint rowIndex = 2;
                foreach (var line in ExtractTextLines(htmlContent))
                {
                    var row = new Row { RowIndex = rowIndex++ };
                    row.Append(CreateInlineStringCell(line));
                    sheetData.Append(row);
                }
            }

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph CreateTitleParagraph(string title)
    {
        return new Paragraph(
            new DocumentFormat.OpenXml.Wordprocessing.RunProperties(new DocumentFormat.OpenXml.Wordprocessing.Bold()),
            new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(title) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Table CreateWordTable(List<List<string>> rows)
    {
        var table = new DocumentFormat.OpenXml.Wordprocessing.Table();
        table.AppendChild(new TableProperties(
            new TableBorders(
                new DocumentFormat.OpenXml.Wordprocessing.TopBorder { Val = BorderValues.Single, Size = 8 },
                new DocumentFormat.OpenXml.Wordprocessing.BottomBorder { Val = BorderValues.Single, Size = 8 },
                new DocumentFormat.OpenXml.Wordprocessing.LeftBorder { Val = BorderValues.Single, Size = 8 },
                new DocumentFormat.OpenXml.Wordprocessing.RightBorder { Val = BorderValues.Single, Size = 8 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 8 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 8 })));

        foreach (var rowCells in rows)
        {
            var row = new TableRow();
            foreach (var cellText in rowCells)
            {
                var cell = new TableCell(new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(cellText) { Space = SpaceProcessingModeValues.Preserve })));
                cell.Append(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto }));
                row.Append(cell);
            }

            table.Append(row);
        }

        return table;
    }

    private static Cell CreateInlineStringCell(string value)
    {
        return new Cell
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(value ?? string.Empty))
        };
    }

    private static List<List<string>> ExtractTableRowsFromHtml(string html)
    {
        var rows = new List<List<string>>();
        var tableMatch = Regex.Match(html, @"<table\b[^>]*>(.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!tableMatch.Success)
        {
            return rows;
        }

        var tableHtml = tableMatch.Groups[1].Value;
        var rowMatches = Regex.Matches(tableHtml, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match rowMatch in rowMatches)
        {
            var cells = new List<string>();
            var cellMatches = Regex.Matches(rowMatch.Groups[1].Value, @"<(th|td)\b[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match cellMatch in cellMatches)
            {
                cells.Add(CleanHtmlText(cellMatch.Groups[2].Value));
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        return rows;
    }

    private static List<string> ExtractTextLines(string html)
    {
        var normalized = html;
        normalized = Regex.Replace(normalized, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"</(p|div|li|tr|h1|h2|h3|h4|h5|h6)>", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"<[^>]+>", " ", RegexOptions.Singleline);
        normalized = WebUtility.HtmlDecode(normalized);

        return normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string CleanHtmlText(string value)
    {
        var withoutTags = Regex.Replace(value, @"<[^>]+>", " ", RegexOptions.Singleline);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
