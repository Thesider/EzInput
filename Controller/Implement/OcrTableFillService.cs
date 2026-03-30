using Controller.Interface;
using Controller.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Controller.Implement;

public class OcrTableFillService : IOcrTableFillService
{
    private readonly double _autoFillThreshold;

    public OcrTableFillService(IConfiguration configuration)
    {
        var parsed = double.TryParse(configuration["AiPipeline:TemplateFill:AutoFillThreshold"], out var threshold)
            ? threshold
            : 0.7;

        _autoFillThreshold = Math.Clamp(parsed, 0.1, 1.0);
    }

    public Task<OcrTableFillResult> FillAsync(string templateHtml, string ocrText, string heuristicMode = "auto", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateHtml))
        {
            return Task.FromResult(OcrTableFillResult.Fail("Template HTML is empty."));
        }

        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(templateHtml);

        var lines = (ocrText ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var structuredFallback = BuildStructuredTableFallback(lines, heuristicMode ?? "auto");
        var structuredFallbackHtml = structuredFallback.Html;

        var fieldNodes = htmlDocument.DocumentNode.SelectNodes("//*[@data-field]");
        if (fieldNodes is null)
        {
            var hasStructuredFallback = !string.IsNullOrWhiteSpace(structuredFallbackHtml);
            return Task.FromResult(new OcrTableFillResult
            {
                Success = true,
                ExtractedText = ocrText ?? string.Empty,
                FilledHtml = structuredFallbackHtml ?? templateHtml,
                MatchedCount = 0,
                RequiredCount = 0,
                CanAutoFill = false,
                UsedStructuredFallback = hasStructuredFallback,
                RejectedLines = structuredFallback.RejectedLines,
                MissingFields = ["No data-field markers found in template."]
            });
        }
        var requiredCount = fieldNodes.Count;

        var indexedPairs = BuildIndexedPairs(lines);

        var matchedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingFields = new List<string>();

        foreach (var fieldNode in fieldNodes)
        {
            var fieldKey = fieldNode.GetAttributeValue("data-field", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fieldKey))
            {
                continue;
            }

            var fieldLabel = fieldNode.GetAttributeValue("data-label", string.Empty).Trim();
            var candidates = BuildCandidates(fieldKey, fieldLabel);
            var value = FindValue(lines, indexedPairs, candidates);

            if (!string.IsNullOrWhiteSpace(value))
            {
                matchedFields[fieldKey] = value;
            }
            else
            {
                missingFields.Add(fieldKey);
            }
        }

        var matchedCount = matchedFields.Count;
        var canAutoFill = requiredCount > 0 && ((double)matchedCount / requiredCount) >= _autoFillThreshold;
        var usedStructuredFallback = matchedCount == 0 && !string.IsNullOrWhiteSpace(structuredFallbackHtml);

        if (matchedCount > 0)
        {
            foreach (var fieldNode in fieldNodes)
            {
                var fieldKey = fieldNode.GetAttributeValue("data-field", string.Empty).Trim();
                if (matchedFields.TryGetValue(fieldKey, out var value))
                {
                    fieldNode.InnerHtml = HtmlEntity.Entitize(value);
                }
            }
        }

        return Task.FromResult(new OcrTableFillResult
        {
            Success = true,
            ExtractedText = ocrText ?? string.Empty,
            FilledHtml = matchedCount > 0
                ? htmlDocument.DocumentNode.OuterHtml
                : (structuredFallbackHtml ?? templateHtml),
            MatchedFields = matchedFields,
            MissingFields = missingFields,
            MatchedCount = matchedCount,
            RequiredCount = requiredCount,
            CanAutoFill = canAutoFill,
            UsedStructuredFallback = usedStructuredFallback,
            RejectedLines = structuredFallback.RejectedLines
        });
    }

    private sealed class StructuredTableParseResult
    {
        public string? Html { get; init; }

        public List<string> RejectedLines { get; init; } = [];
    }

    private static List<string> BuildCandidates(string key, string label)
    {
        var normalizedKey = key.Replace("_", " ").Replace("-", " ").Trim();
        var deCamelKey = Regex.Replace(normalizedKey, "(?<=[a-z])(?=[A-Z])", " ");

        var candidates = new List<string> { key, normalizedKey, deCamelKey };
        if (!string.IsNullOrWhiteSpace(label))
        {
            candidates.Add(label);
        }

        return candidates
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> BuildIndexedPairs(List<string> lines)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (TryParseSchemaRow(line, out var schemaCol1, out var schemaCol2, out var schemaCol3))
            {
                var key = NormalizeToken(schemaCol1);
                var value = string.IsNullOrWhiteSpace(schemaCol3)
                    ? schemaCol2
                    : $"{schemaCol2} | {schemaCol3}";

                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value) && !pairs.ContainsKey(key))
                {
                    pairs[key] = value;
                }
            }

            var keyValueMatch = Regex.Match(line, "^(.+?)\\s*[:\\-]\\s*(.+)$", RegexOptions.IgnoreCase);
            if (keyValueMatch.Success)
            {
                var key = NormalizeToken(keyValueMatch.Groups[1].Value);
                var value = keyValueMatch.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value) && !pairs.ContainsKey(key))
                {
                    pairs[key] = value;
                }
            }

            // Also try label+value on same line (e.g., "Donations $ 64,285.95")
            var labelValueMatch = Regex.Match(line, @"^(.+?)\s+\$\s*([\d,]+(?:\.\d+)?)$");
            if (labelValueMatch.Success)
            {
                var key = NormalizeToken(labelValueMatch.Groups[1].Value);
                var value = NormalizeCurrencyValue(labelValueMatch.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value) && !pairs.ContainsKey(key))
                {
                    pairs[key] = value;
                }
            }

            var tokens = SplitLineTokens(line);
            if (tokens.Count >= 2)
            {
                var key = NormalizeToken(tokens[0]);
                var value = string.Join(" | ", tokens.Skip(1)).Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value) && !pairs.ContainsKey(key))
                {
                    pairs[key] = value;
                }
            }
        }

        return pairs;
    }

    private static List<string> SplitLineTokens(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        string[] tokens;
        if (line.Contains('|'))
        {
            tokens = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
        }
        else if (line.Contains('\t'))
        {
            tokens = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            tokens = Regex.Split(line, "\\s{2,}");
        }

        return tokens
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string NormalizeToken(string value)
    {
        var normalized = Regex.Replace(value ?? string.Empty, "[^a-zA-Z0-9]", string.Empty);
        return normalized.ToLowerInvariant();
    }

    private static string? FindValue(List<string> lines, Dictionary<string, string> indexedPairs, List<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalizedCandidate = NormalizeToken(candidate);
            if (!string.IsNullOrWhiteSpace(normalizedCandidate) && indexedPairs.TryGetValue(normalizedCandidate, out var indexedValue))
            {
                return indexedValue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                var fuzzyMatch = indexedPairs.FirstOrDefault(pair =>
                    pair.Key.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
                    normalizedCandidate.Contains(pair.Key, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(fuzzyMatch.Value))
                {
                    return fuzzyMatch.Value;
                }
            }

            var escapedCandidate = Regex.Escape(candidate);
            var pairPattern = $"^{escapedCandidate}\\s*[:\\-]\\s*(.+)$";

            foreach (var line in lines)
            {
                var match = Regex.Match(line, pairPattern, RegexOptions.IgnoreCase);
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    return match.Groups[1].Value.Trim();
                }
            }
        }

        for (var index = 0; index < lines.Count - 1; index++)
        {
            foreach (var candidate in candidates)
            {
                if (lines[index].Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return lines[index + 1].Trim();
                }
            }
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────
    // Dynamic multi-strategy table builder
    // ──────────────────────────────────────────────────────────────

    private static StructuredTableParseResult BuildStructuredTableFallback(List<string> rawLines, string heuristicMode)
    {
        var cleaned = rawLines.Select(CleanOcrLine).ToList();

        // Strategy 1: Detect vertical column layout (e.g., simpletable.png)
        var vertical = DetectVerticalColumnLayout(cleaned);
        if (vertical is not null)
        {
            return vertical;
        }

        // Strategy 2: Detect label+value pairs on same line (e.g., 2x7.png, test.png)
        var labelValue = DetectLabelValuePairLayout(cleaned, heuristicMode);
        if (labelValue is not null)
        {
            return labelValue;
        }

        // Strategy 3: Detect row-by-row pipe/whitespace delimited tables (e.g., 3x5.png)
        var rowByRow = DetectRowByRowLayout(cleaned, heuristicMode);
        if (rowByRow is not null)
        {
            return rowByRow;
        }

        return new StructuredTableParseResult { Html = null, RejectedLines = cleaned };
    }

    // ──────────────────────────────────────────────────────────────
    // OCR line cleaning — remove noise, fix common misreads
    // ──────────────────────────────────────────────────────────────

    private static string CleanOcrLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var s = line.Trim();

        // Remove standalone bracket noise at edges: "[", "]", "{", "}"
        s = Regex.Replace(s, @"^\s*[\[\]{}]+\s*", string.Empty);
        s = Regex.Replace(s, @"\s*[\[\]{}]+\s*$", string.Empty);

        // Fix misplaced pipes at start of line: "|DociorName" → "DociorName"
        s = Regex.Replace(s, @"^\|\s*", string.Empty);

        // Fix OCR misreads for SQL types
        s = Regex.Replace(s, @"\bInyarchar\b", "nvarchar", RegexOptions.IgnoreCase);
        // Fix: nvarchar0) / nvarcha0) / varchar0) → nvarchar(50)
        // Covers cases where OCR drops "(5" or similar digits from type definitions
        s = Regex.Replace(s, @"n?varchar[01l]?\)", "nvarchar(50)", RegexOptions.IgnoreCase);
        // Collapse double parens from chained fixes: "nvarchar(50))" → "nvarchar(50)"
        s = Regex.Replace(s, @"nvarchar\((\d+)\)\)", "nvarchar($1)");

        // Normalize multiple spaces
        s = Regex.Replace(s, @"\s+", " ").Trim();

        return s;
    }

    // ──────────────────────────────────────────────────────────────
    // Strategy 1: Vertical column layout
    // OCR reads each column top-to-bottom:
    //   [Header1, RowLabel1, ..., RowLabelN, Header2, Value1, ..., ValueN]
    // We detect the transition from text-only lines to currency lines,
    // then pair row labels with values.
    // ──────────────────────────────────────────────────────────────

    private static StructuredTableParseResult? DetectVerticalColumnLayout(List<string> lines)
    {
        if (lines.Count < 4)
        {
            return null;
        }

        var rejectedLines = new List<string>();

        // Find the first currency/percent line
        var firstCurrencyIdx = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = CleanCell(lines[i]);
            if (Regex.IsMatch(trimmed, @"^\$\s*[\d,]+(?:\.\d+)?$")
                || Regex.IsMatch(trimmed, @"^[\d,]+(?:\.\d+)?\s*%$"))
            {
                firstCurrencyIdx = i;
                break;
            }
        }

        if (firstCurrencyIdx < 2)
        {
            return null; // need at least header + 1 label + 1 value
        }

        // Count currency lines from firstCurrencyIdx onward
        var dataCount = 0;
        for (var i = firstCurrencyIdx; i < lines.Count; i++)
        {
            var trimmed = CleanCell(lines[i]);
            if (Regex.IsMatch(trimmed, @"^\$\s*[\d,]+(?:\.\d+)?$")
                || Regex.IsMatch(trimmed, @"^[\d,]+(?:\.\d+)?\s*%$"))
            {
                dataCount++;
            }
            else if (!string.IsNullOrWhiteSpace(trimmed))
            {
                break; // non-currency line ends the data block
            }
        }

        if (dataCount < 1)
        {
            return null;
        }

        // The line just before firstCurrencyIdx should be a text header (e.g., "Price")
        var headerIdx = firstCurrencyIdx - 1;
        var headerText = CleanCell(lines[headerIdx]);

        // The text before that header should contain the row labels
        // We need exactly `dataCount` labels from lines[0..headerIdx-1]
        var availableLabels = lines.Take(headerIdx)
            .Select(CleanCell)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (availableLabels.Count < dataCount)
        {
            return null;
        }

        // Verify the labels are text (not currency/numeric)
        var allLabelsAreText = availableLabels.All(l =>
            !Regex.IsMatch(l, @"^\$") && !Regex.IsMatch(l, @"^\d"));

        if (!allLabelsAreText)
        {
            return null;
        }

        // Take the last `dataCount` labels (skip any super-headers like "Fruit")
        var rowLabels = availableLabels.TakeLast(dataCount).ToList();

        // Collect the actual data values
        var dataValues = new List<string>();
        for (var i = firstCurrencyIdx; i < lines.Count && dataValues.Count < dataCount; i++)
        {
            var trimmed = CleanCell(lines[i]);
            if (Regex.IsMatch(trimmed, @"^\$\s*[\d,]+(?:\.\d+)?$")
                || Regex.IsMatch(trimmed, @"^[\d,]+(?:\.\d+)?\s*%$"))
            {
                dataValues.Add(trimmed);
            }
        }

        if (rowLabels.Count != dataValues.Count || dataValues.Count < 2)
        {
            return null;
        }

        // Build HTML table with 3 columns: Label, headerText (e.g., "Price"), empty Notes
        var html = new System.Text.StringBuilder();
        html.Append("<table><thead><tr>");
        html.Append("<th>").Append(HtmlEntity.Entitize("Column")).Append("</th>");
        html.Append("<th>").Append(HtmlEntity.Entitize(headerText)).Append("</th>");
        html.Append("<th>").Append(HtmlEntity.Entitize("Notes")).Append("</th>");
        html.Append("</tr></thead><tbody>");

        for (var i = 0; i < rowLabels.Count; i++)
        {
            html.Append("<tr>");
            html.Append("<td>").Append(HtmlEntity.Entitize(rowLabels[i])).Append("</td>");
            html.Append("<td>").Append(HtmlEntity.Entitize(dataValues[i])).Append("</td>");
            html.Append("<td></td>");
            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
        return new StructuredTableParseResult { Html = html.ToString(), RejectedLines = rejectedLines };
    }

    private static string BuildVerticalTableHtml(List<string> headers, List<string> dataValues)
    {
        var html = new System.Text.StringBuilder();
        html.Append("<table><thead><tr>");
        for (var hi = 0; hi < Math.Min(headers.Count, 3); hi++)
        {
            html.Append("<th>").Append(HtmlEntity.Entitize(headers[hi])).Append("</th>");
        }

        if (headers.Count < 3)
        {
            html.Append("<th>").Append(HtmlEntity.Entitize("Value")).Append("</th>");
        }

        html.Append("</tr></thead><tbody>");

        for (var rowIdx = 0; rowIdx < dataValues.Count; rowIdx++)
        {
            html.Append("<tr>");
            html.Append("<td>").Append(HtmlEntity.Entitize(headers[rowIdx % headers.Count])).Append("</td>");
            html.Append("<td>").Append(HtmlEntity.Entitize(dataValues[rowIdx])).Append("</td>");
            if (headers.Count < 3)
            {
                html.Append("<td></td>");
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
        return html.ToString();
    }

    // ──────────────────────────────────────────────────────────────
    // Strategy 2: Label + value pairs on the same line
    // Handles cases like: "Keyboard $25,000" or "Donations $64,285.95"
    // ──────────────────────────────────────────────────────────────

    private static StructuredTableParseResult? DetectLabelValuePairLayout(List<string> lines, string heuristicMode)
    {
        var tableStart = FindTableStartIndex(lines);
        var candidateLines = lines.Skip(tableStart).ToList();

        var parsedRows = new List<List<string>>();
        var rejectedLines = new List<string>();

        for (var i = 0; i < candidateLines.Count; i++)
        {
            var line = candidateLines[i];

            if (string.IsNullOrWhiteSpace(line) || IsHeaderLikeRow(line))
            {
                continue;
            }

            // Try currency pattern first: "Label $ amount"
            var currencyMatch = Regex.Match(
                line,
                @"^(.+?)\s+\$\s*([\d,]+(?:\.\d+)?)$",
                RegexOptions.IgnoreCase);

            if (currencyMatch.Success)
            {
                var label = CleanCell(currencyMatch.Groups[1].Value);
                var amount = NormalizeCurrencyValue(currencyMatch.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(amount))
                {
                    parsedRows.Add([label, amount, string.Empty]);
                    continue;
                }
            }

            // Try percent pattern: "Label 3.5%"
            var percentMatch = Regex.Match(
                line,
                @"^(.+?)\s+([\d,]+(?:\.\d+)?\s*%)$",
                RegexOptions.IgnoreCase);

            if (percentMatch.Success)
            {
                var label = CleanCell(percentMatch.Groups[1].Value);
                var pct = NormalizePercentValue(percentMatch.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(pct))
                {
                    parsedRows.Add([label, pct, string.Empty]);
                    continue;
                }
            }

            // Try parenthesized negative: "(Net Income)" → skip as sub-header
            if (Regex.IsMatch(line, @"^\([^0-9]+\)$"))
            {
                var label = CleanCell(line.Trim('(', ')'));
                parsedRows.Add([label, string.Empty, string.Empty]);
                continue;
            }

            // Try colon/dash separator: "Key: Value"
            var kvMatch = Regex.Match(line, @"^(.+?)\s*[:\-]\s*(.+)$");
            if (kvMatch.Success)
            {
                var label = CleanCell(kvMatch.Groups[1].Value);
                var value = CleanCell(kvMatch.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value))
                {
                    parsedRows.Add([label, value, string.Empty]);
                    continue;
                }
            }

            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                rejectedLines.Add(trimmed);
            }
        }

        if (parsedRows.Count < 2)
        {
            return null;
        }

        // Decide if this is truly a label+value layout:
        // More than half of rows should have a non-empty second column
        var rowsWithValues = parsedRows.Count(r => r.Count > 1 && !string.IsNullOrWhiteSpace(r[1]));
        if (rowsWithValues < parsedRows.Count / 2)
        {
            return null;
        }

        // Check that column 1 looks like labels (mostly alphabetic, not data types)
        var looksLikeLabels = parsedRows.Count(r =>
        {
            var c = r[0];
            return !string.IsNullOrWhiteSpace(c) && !TrySplitSqlTypeAndConstraint(c, out _, out _);
        });
        if (looksLikeLabels < parsedRows.Count / 2)
        {
            return null;
        }

        var headers = InferHeaders(parsedRows, heuristicMode);
        var html = BuildTableHtml(headers, parsedRows);
        return new StructuredTableParseResult { Html = html, RejectedLines = rejectedLines };
    }

    // ──────────────────────────────────────────────────────────────
    // Strategy 3: Row-by-row pipe/whitespace delimited tables
    // Handles schema tables like: "ID int PK, Identity"
    // ──────────────────────────────────────────────────────────────

    private static StructuredTableParseResult? DetectRowByRowLayout(List<string> lines, string heuristicMode)
    {
        var tableStart = FindTableStartIndex(lines);
        var candidateLines = lines.Skip(tableStart).ToList();

        var parsedRows = new List<List<string>>();
        var rejectedLines = new List<string>();

        // Detect explicit header row
        var headerLineIndex = -1;
        List<string>? detectedHeaders = null;
        for (var hi = 0; hi < candidateLines.Count; hi++)
        {
            var tokens = SplitLineTokens(candidateLines[hi]);
            if (tokens.Count >= 2)
            {
                var allAlpha = tokens.All(t => Regex.IsMatch(t, "[A-Za-z]") && !Regex.IsMatch(t, "\\d") && t.Trim().Length <= 40);
                if (allAlpha)
                {
                    headerLineIndex = hi;
                    detectedHeaders = tokens.Select(CleanCell).ToList();
                    break;
                }
            }
        }

        for (var i = 0; i < candidateLines.Count; i++)
        {
            if (i == headerLineIndex)
            {
                continue;
            }

            var line = candidateLines[i];
            if (string.IsNullOrWhiteSpace(line) || IsHeaderLikeRow(line))
            {
                continue;
            }

            // Try schema row parsing (pipe-delimited, SQL types, currency, percent)
            if (TryParseSchemaRow(line, out var col1, out var col2, out var col3, heuristicMode))
            {
                parsedRows.Add([col1, col2, col3]);
                continue;
            }

            // Try pipe/tab/multi-space tokenized rows
            var tokens = SplitLineTokens(line);
            if (tokens.Count >= 2)
            {
                if (tokens.Count >= 3 || LooksLikeSchemaRow(tokens))
                {
                    parsedRows.Add(NormalizeRowToThreeColumns(tokens));
                    continue;
                }

                // 2-token row: try to split label from value
                var cleaned0 = CleanCell(tokens[0]);
                var cleaned1 = CleanCell(tokens[1]);
                if (!string.IsNullOrWhiteSpace(cleaned0) && !string.IsNullOrWhiteSpace(cleaned1))
                {
                    parsedRows.Add([cleaned0, cleaned1, string.Empty]);
                    continue;
                }
            }

            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                rejectedLines.Add(trimmed);
            }
        }

        if (parsedRows.Count == 0)
        {
            return null;
        }

        List<string> headers;
        if (detectedHeaders is not null && detectedHeaders.Count > 0)
        {
            while (detectedHeaders.Count < 3)
            {
                detectedHeaders.Add(string.Empty);
            }

            headers = detectedHeaders.Take(3).ToList();
        }
        else
        {
            headers = InferHeaders(parsedRows, heuristicMode);
        }

        var html = BuildTableHtml(headers, parsedRows);
        return new StructuredTableParseResult { Html = html, RejectedLines = rejectedLines };
    }

    // ──────────────────────────────────────────────────────────────
    // Shared helpers
    // ──────────────────────────────────────────────────────────────

    private static string BuildTableHtml(List<string> headers, List<List<string>> rows)
    {
        var html = new System.Text.StringBuilder();
        html.Append("<table><thead><tr>");
        foreach (var header in headers.Take(3))
        {
            html.Append("<th>").Append(HtmlEntity.Entitize(header)).Append("</th>");
        }
        html.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            var columns = NormalizeRowToThreeColumns(row);
            html.Append("<tr>");
            foreach (var column in columns)
            {
                html.Append("<td>").Append(HtmlEntity.Entitize(column)).Append("</td>");
            }
            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
        return html.ToString();
    }

    private static int FindTableStartIndex(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsHeaderLikeRow(line))
            {
                return i;
            }

            if (TryParseSchemaRow(line, out _, out _, out _))
            {
                return i;
            }

            // Also detect currency/percent lines as table start
            if (Regex.IsMatch(line, @"^.+\s+\$\s*[\d,]+", RegexOptions.IgnoreCase))
            {
                return i;
            }

            if (Regex.IsMatch(line, @"^.+\s+[\d,]+(?:\.\d+)?\s*%", RegexOptions.IgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static List<string> InferHeaders(List<List<string>> rows, string heuristicMode)
    {
        if (rows is null || rows.Count == 0)
        {
            return new List<string> { "Column", "Value", "Notes" };
        }

        var mode = (heuristicMode ?? "auto").Trim().ToLowerInvariant();
        if (mode == "schema")
        {
            return new List<string> { "Column", "Data Type", "Constraints" };
        }

        if (mode == "labelvalue" || mode == "label_value" || mode == "value")
        {
            return new List<string> { "Column", "Value", "Notes" };
        }

        // Auto-detect: analyze column content patterns
        var total = rows.Count;
        var sqlTypeCount = 0;
        var numericSecondCount = 0;
        var numericThirdCount = 0;
        var percentSecondCount = 0;
        var percentThirdCount = 0;
        var currencySecondCount = 0;
        var currencyThirdCount = 0;
        var nonEmptySecond = 0;
        var nonEmptyThird = 0;

        foreach (var r in rows)
        {
            var c2 = r.Count > 1 ? r[1] : string.Empty;
            var c3 = r.Count > 2 ? r[2] : string.Empty;

            if (!string.IsNullOrWhiteSpace(c2)) nonEmptySecond++;
            if (!string.IsNullOrWhiteSpace(c3)) nonEmptyThird++;

            if (TrySplitSqlTypeAndConstraint(c2, out _, out _)) sqlTypeCount++;

            if (Regex.IsMatch(c2, "[0-9]") && !Regex.IsMatch(c2, "[A-Za-z]")) numericSecondCount++;
            if (Regex.IsMatch(c3, "[0-9]") && !Regex.IsMatch(c3, "[A-Za-z]")) numericThirdCount++;

            if (c2.Contains("%")) percentSecondCount++;
            if (c3.Contains("%")) percentThirdCount++;
            if (c2.Contains("$")) currencySecondCount++;
            if (c3.Contains("$")) currencyThirdCount++;
        }

        // If majority of rows declare SQL types in column 2, treat as schema-like
        if (sqlTypeCount >= Math.Ceiling(total * 0.5))
        {
            return new List<string> { "Column", "Data Type", "Constraints" };
        }

        // If numeric/currency/percent values appear mostly in third column
        if (numericThirdCount >= Math.Ceiling(total * 0.5) || (sqlTypeCount > 0 && numericThirdCount > 0))
        {
            return new List<string> { "Column", "Data Type", "Value" };
        }

        // If numeric/currency/percent values appear mostly in second column, assume label/value
        if (numericSecondCount >= Math.Ceiling(total * 0.5)
            || percentSecondCount >= Math.Ceiling(total * 0.5)
            || currencySecondCount >= Math.Ceiling(total * 0.5))
        {
            return new List<string> { "Column", "Value", "Notes" };
        }

        // Fallback: if third column is mostly empty, present two-column style
        if (nonEmptyThird < Math.Ceiling(total * 0.33))
        {
            return new List<string> { "Column", "Value", "Notes" };
        }

        return new List<string> { "Column", "Data Type", "Constraints" };
    }

    private static bool LooksLikeSchemaRow(List<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return false;
        }

        var second = CleanCell(tokens[1]);
        if (TrySplitSqlTypeAndConstraint(second, out _, out _))
        {
            return true;
        }

        if (TrySplitColumnAndType(CleanCell(tokens[0]), out _, out _))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseSchemaRow(string line, out string col1, out string col2, out string col3, string heuristicMode = "auto")
    {
        col1 = string.Empty;
        col2 = string.Empty;
        col3 = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var normalizedLine = line.Trim().Trim('|').Trim();
        if (IsHeaderLikeRow(normalizedLine))
        {
            return false;
        }

        var mode = (heuristicMode ?? "auto").Trim().ToLowerInvariant();

        // Detect currency-style rows like: "Donations $ 64,285.95" or "Iran $ 9767321"
        var currencyMatch = Regex.Match(
            normalizedLine,
            @"^(.+?)\s+\$\s*([\d,]+(?:\.\d+)?)$",
            RegexOptions.IgnoreCase);

        if (currencyMatch.Success)
        {
            col1 = CleanCell(currencyMatch.Groups[1].Value);
            if (mode == "labelvalue" || mode == "label_value" || mode == "value")
            {
                col2 = NormalizeCurrencyValue(currencyMatch.Groups[2].Value);
                col3 = string.Empty;
            }
            else
            {
                col2 = "decimal(18,2)";
                col3 = NormalizeCurrencyValue(currencyMatch.Groups[2].Value);
            }

            return true;
        }

        // Detect percent-style rows like: "Marketing/Admin (% of Revenue) 3.5%"
        var percentMatch = Regex.Match(
            normalizedLine,
            @"^(.+?)\s+([\d,]+(?:\.\d+)?\s*%)$",
            RegexOptions.IgnoreCase);

        if (percentMatch.Success)
        {
            col1 = CleanCell(percentMatch.Groups[1].Value);
            if (mode == "labelvalue" || mode == "label_value" || mode == "value")
            {
                col2 = NormalizePercentValue(percentMatch.Groups[2].Value);
                col3 = string.Empty;
            }
            else
            {
                col2 = "decimal(5,2)";
                col3 = NormalizePercentValue(percentMatch.Groups[2].Value);
            }

            return true;
        }

        // Handle single parenthesized label rows like: "(Net Income)"
        if (Regex.IsMatch(normalizedLine, @"^\([^0-9]+\)$"))
        {
            col1 = CleanCell(normalizedLine.Trim('(', ')'));
            col2 = "nvarchar(255)";
            col3 = string.Empty;
            return true;
        }

        var pipeTokens = normalizedLine
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (pipeTokens.Count >= 3)
        {
            col1 = CleanCell(pipeTokens[0]);
            col2 = NormalizeDataTypeText(CleanCell(pipeTokens[1]));
            col3 = CleanCell(string.Join(" | ", pipeTokens.Skip(2)));
            return true;
        }

        if (pipeTokens.Count == 2)
        {
            var left = CleanCell(pipeTokens[0]);
            var right = CleanCell(pipeTokens[1]);
            if (!string.IsNullOrWhiteSpace(left) && TrySplitSqlTypeAndConstraint(right, out var parsedType, out var parsedConstraint))
            {
                col1 = left;
                col2 = parsedType;
                col3 = parsedConstraint;
                return true;
            }

            if (TrySplitColumnAndType(left, out var parsedColumn, out var parsedLeftType) && !string.IsNullOrWhiteSpace(right))
            {
                col1 = parsedColumn;
                col2 = parsedLeftType;
                col3 = right;
                return true;
            }
        }

        var sqlTypeMatch = Regex.Match(
            normalizedLine,
            @"(?:n?varchar\s*\(\s*(?:\d+|max)\s*\)|char\s*\(\s*\d+\s*\)|int|bigint|smallint|tinyint|bit|date|datetime2?|time|decimal\s*\(\s*\d+\s*,\s*\d+\s*\)|numeric\s*\(\s*\d+\s*,\s*\d+\s*\)|float|real|uniqueidentifier|text|ntext)(?=\s|\||$|\)|\])",
            RegexOptions.IgnoreCase);

        if (!sqlTypeMatch.Success)
        {
            return false;
        }

        col1 = CleanCell(normalizedLine[..sqlTypeMatch.Index]);
        col2 = NormalizeDataTypeText(CleanCell(sqlTypeMatch.Value));
        var rawCol3 = normalizedLine[(sqlTypeMatch.Index + sqlTypeMatch.Length)..];
        // Strip orphaned closing paren from OCR type fix (e.g., ") [Unique License Code")
        rawCol3 = Regex.Replace(rawCol3, @"^\)\s*", string.Empty);
        col3 = CleanCell(rawCol3);

        return !string.IsNullOrWhiteSpace(col1) && !string.IsNullOrWhiteSpace(col2);
    }

    private static bool IsHeaderLikeRow(string line)
    {
        var normalized = NormalizeToken(line);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains("columndatatypeconstraints"))
        {
            return true;
        }

        var hasColumn = normalized.Contains("column") || normalized.Contains("field") || normalized.Contains("name");
        var hasType = normalized.Contains("datatype") || normalized.Contains("type");
        var hasConstraint = normalized.Contains("constraint");

        return hasColumn && hasType && hasConstraint;
    }

    private static List<string> NormalizeRowToThreeColumns(List<string> row)
    {
        if (row.Count >= 3)
        {
            return [
                row[0],
                row[1],
                string.Join(" | ", row.Skip(2))
            ];
        }

        if (row.Count == 2)
        {
            var cleanedFirst = CleanCell(row[0]);
            var cleanedSecond = CleanCell(row[1]);

            if (TrySplitColumnAndType(cleanedFirst, out var parsedColumn, out var parsedType))
            {
                return [parsedColumn, parsedType, cleanedSecond];
            }

            if (TrySplitSqlTypeAndConstraint(row[1], out var parsedDataType, out var parsedConstraint))
            {
                return [cleanedFirst, parsedDataType, parsedConstraint];
            }

            return [cleanedFirst, cleanedSecond, string.Empty];
        }

        return [string.Join(" ", row), string.Empty, string.Empty];
    }

    private static bool TrySplitColumnAndType(string left, out string column, out string dataType)
    {
        column = string.Empty;
        dataType = string.Empty;

        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        var match = Regex.Match(
            left,
            @"^(.*?)(n?varchar\s*\(\s*(?:\d+|max)\s*\)|char\s*\(\s*\d+\s*\)|int|bigint|smallint|tinyint|bit|date|datetime2?|time|decimal\s*\(\s*\d+\s*,\s*\d+\s*\)|numeric\s*\(\s*\d+\s*,\s*\d+\s*\)|float|real|uniqueidentifier|text|ntext)\s*$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        column = CleanCell(match.Groups[1].Value);
        dataType = NormalizeDataTypeText(CleanCell(match.Groups[2].Value));
        return !string.IsNullOrWhiteSpace(column) && !string.IsNullOrWhiteSpace(dataType);
    }

    private static bool TrySplitSqlTypeAndConstraint(string input, out string dataType, out string constraints)
    {
        dataType = string.Empty;
        constraints = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var match = Regex.Match(
            input,
            @"^(?:\|\s*)?(n?varchar\s*\(\s*(?:\d+|max)\s*\)|char\s*\(\s*\d+\s*\)|int|bigint|smallint|tinyint|bit|date|datetime2?|time|decimal\s*\(\s*\d+\s*,\s*\d+\s*\)|numeric\s*\(\s*\d+\s*,\s*\d+\s*\)|float|real|uniqueidentifier|text|ntext)(?=\s|\||$|\)|\])(.*)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        dataType = NormalizeDataTypeText(CleanCell(match.Groups[1].Value));
        var rawConstraints = match.Groups[2].Value;
        // Strip orphaned closing paren from OCR type fix
        rawConstraints = Regex.Replace(rawConstraints, @"^\)\s*", string.Empty);
        constraints = CleanCell(rawConstraints);
        return !string.IsNullOrWhiteSpace(dataType);
    }

    private static string NormalizeDataTypeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        // Fix any remaining OCR prefix artifacts on varchar types
        normalized = Regex.Replace(normalized, @"^[i1l\|]+(?=n?varchar\s*\()", string.Empty, RegexOptions.IgnoreCase);
        // Normalize "char" without "n" prefix to "nchar" only if it looks like an OCR miss of nvarchar
        // (Don't touch legitimate "char(N)" types)
        return normalized;
    }

    private static string CleanCell(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim();
        cleaned = cleaned.Trim('|', ':', '-', ' ');
        cleaned = Regex.Replace(cleaned, @"^[\[\]{}]+", string.Empty);
        cleaned = Regex.Replace(cleaned, @"[\[\]{}]+$", string.Empty);
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        return cleaned;
    }

    private static string NormalizeCurrencyValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var s = raw.Trim();
        var isParenNegative = s.StartsWith("(") && s.EndsWith(")");
        s = s.Replace("$", "").Replace(" ", "").Replace(",", "");
        if (isParenNegative)
        {
            s = s.Trim('(', ')');
        }

        if (!s.Contains('.'))
        {
            if (s.Length > 2)
            {
                s = s.Insert(s.Length - 2, ".");
            }
            else
            {
                s = "0." + s.PadLeft(2, '0');
            }
        }

        if (!decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
        {
            return raw;
        }

        var formatted = d.ToString("N2", CultureInfo.GetCultureInfo("en-US"));
        if (isParenNegative)
        {
            formatted = "(" + formatted + ")";
        }

        return formatted;
    }

    private static string NormalizePercentValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var s = raw.Trim();
        var isParenNegative = s.StartsWith("(") && s.EndsWith(")");
        s = s.Replace("%", "").Replace(" ", "").Replace(",", "");
        if (isParenNegative)
        {
            s = s.Trim('(', ')');
        }

        if (!decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
        {
            return raw;
        }

        var formatted = d.ToString("N2", CultureInfo.GetCultureInfo("en-US")) + "%";
        if (isParenNegative)
        {
            formatted = "(" + formatted + ")";
        }

        return formatted;
    }
}
