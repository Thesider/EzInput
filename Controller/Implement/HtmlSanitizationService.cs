using Controller.Interface;
using Ganss.Xss;
using System.Text.RegularExpressions;

namespace Controller.Implement;

public class HtmlSanitizationService : IHtmlSanitizationService
{
    private static readonly HtmlSanitizer Sanitizer = BuildSanitizer();

    public string SanitizeRichText(string html)
    {
        return Sanitizer.Sanitize(html ?? string.Empty);
    }

    public string SanitizePlainText(string input)
    {
        var normalized = Regex.Replace(input ?? string.Empty, "\\s+", " ").Trim();
        return normalized;
    }

    private static HtmlSanitizer BuildSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // Preserve CKEditor rich text and table-related tags.
        sanitizer.AllowedTags.UnionWith(new[]
        {
            "p", "br", "strong", "b", "em", "i", "u", "s", "blockquote",
            "ul", "ol", "li", "a", "h1", "h2", "h3", "h4", "h5", "h6",
            "table", "thead", "tbody", "tr", "td", "th", "caption", "colgroup", "col"
        });

        sanitizer.AllowedAttributes.UnionWith(new[]
        {
            "href", "title", "target", "rel", "colspan", "rowspan", "scope"
        });

        sanitizer.AllowedSchemes.UnionWith(new[] { "http", "https", "mailto" });

        sanitizer.KeepChildNodes = true;

        return sanitizer;
    }
}
