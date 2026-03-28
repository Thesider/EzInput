using Controller.Interface;
using Controller.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

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

    public Task<OcrTableFillResult> FillAsync(string templateHtml, string ocrText, CancellationToken cancellationToken = default)
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

        var structuredFallback = BuildStructuredTableFallback(lines);
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

    private static StructuredTableParseResult BuildStructuredTableFallback(List<string> lines)
    {
        var tableStartIndex = FindTableStartIndex(lines);
        var candidateLines = lines.Skip(tableStartIndex).ToList();

        var parsedRows = new List<List<string>>();
        var rejectedLines = new List<string>();
        foreach (var line in candidateLines)
        {
            if (IsHeaderLikeRow(line))
            {
                continue;
            }

            if (TryParseSchemaRow(line, out var col1, out var col2, out var col3))
            {
                parsedRows.Add([col1, col2, col3]);
                continue;
            }

            var tokens = SplitLineTokens(line);
            if (tokens.Count >= 3 && LooksLikeSchemaRow(tokens))
            {
                parsedRows.Add(NormalizeRowToThreeColumns(tokens));
                continue;
            }

            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                rejectedLines.Add(trimmed);
            }
        }

        if (parsedRows.Count == 0)
        {
            return new StructuredTableParseResult
            {
                Html = null,
                RejectedLines = rejectedLines
            };
        }

        List<string> headers = ["Column", "Data Type", "Constraints"];
        var dataRows = parsedRows;

        var html = new System.Text.StringBuilder();
        html.Append("<table><thead><tr>");
        foreach (var header in headers.Take(3))
        {
            html.Append("<th>").Append(HtmlEntity.Entitize(header)).Append("</th>");
        }
        html.Append("</tr></thead><tbody>");

        foreach (var row in dataRows)
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
        return new StructuredTableParseResult
        {
            Html = html.ToString(),
            RejectedLines = rejectedLines
        };
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
        }

        return 0;
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

    private static bool TryParseSchemaRow(string line, out string col1, out string col2, out string col3)
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
            @"\b(?:n?varchar\s*\(\s*(?:\d+|max)\s*\)|char\s*\(\s*\d+\s*\)|int|bigint|smallint|tinyint|bit|date|datetime2?|time|decimal\s*\(\s*\d+\s*,\s*\d+\s*\)|numeric\s*\(\s*\d+\s*,\s*\d+\s*\)|float|real|uniqueidentifier|text|ntext)(?=\s|\||$)",
            RegexOptions.IgnoreCase);

        if (!sqlTypeMatch.Success)
        {
            return false;
        }

        col1 = CleanCell(normalizedLine[..sqlTypeMatch.Index]);
        col2 = NormalizeDataTypeText(CleanCell(sqlTypeMatch.Value));
        col3 = CleanCell(normalizedLine[(sqlTypeMatch.Index + sqlTypeMatch.Length)..]);

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
            @"^(.*?)([i1l\|]?n?varchar\s*\(\s*(?:\d+|max)\s*\)|char\s*\(\s*\d+\s*\)|int|bigint|smallint|tinyint|bit|date|datetime2?|time|decimal\s*\(\s*\d+\s*,\s*\d+\s*\)|numeric\s*\(\s*\d+\s*,\s*\d+\s*\)|float|real|uniqueidentifier|text|ntext)\s*$",
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
            @"^(?:\|\s*)?([i1l\|]?n?varchar\s*\(\s*(?:\d+|max)\s*\)|char\s*\(\s*\d+\s*\)|int|bigint|smallint|tinyint|bit|date|datetime2?|time|decimal\s*\(\s*\d+\s*,\s*\d+\s*\)|numeric\s*\(\s*\d+\s*,\s*\d+\s*\)|float|real|uniqueidentifier|text|ntext)(?=\s|\||$)(.*)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        dataType = NormalizeDataTypeText(CleanCell(match.Groups[1].Value));
        constraints = CleanCell(match.Groups[2].Value);
        return !string.IsNullOrWhiteSpace(dataType);
    }

    private static string NormalizeDataTypeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        normalized = Regex.Replace(normalized, @"^[i1l\|]+(?=n?varchar\s*\()", string.Empty, RegexOptions.IgnoreCase);
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
}
