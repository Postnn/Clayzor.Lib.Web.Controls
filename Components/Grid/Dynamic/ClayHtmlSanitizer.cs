using System.Text.RegularExpressions;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Санитизация HTML: вырезает потенциально опасные теги и атрибуты.
/// </summary>
public static class ClayHtmlSanitizer
{
    private static readonly Regex _scriptTag    = new(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex _eventAttrs   = new(@"\s+on\w+\s*=\s*""[^""]*""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _javascript   = new(@"javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Вырезает теги &lt;script&gt;, onXxx-атрибуты и javascript:-схемы.
    /// </summary>
    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html ?? "";

        var result = _scriptTag.Replace(html, "");
        result     = _eventAttrs.Replace(result, "");
        result     = _javascript.Replace(result, "");

        return result;
    }
}
