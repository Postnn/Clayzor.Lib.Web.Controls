namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Зарегистрированный путь справки формы, включая прикладной префикс и раздел.</summary>
public sealed record ClayFormHelp(string DocumentationPath);

/// <summary>Строит ссылку только из настроенной базы и метаданных формы.</summary>
public static class ClayFormHelpUrl
{
    /// <summary>
    /// Принимает базу-каталог HTTP(S) или legacy query-префикс,
    /// который заканчивается присваиванием параметру (например, <c>?page=</c>).
    /// </summary>
    public static string? Resolve(string? configuredBase, ClayFormHelp? form)
    {
        if (string.IsNullOrWhiteSpace(configuredBase) ||
            configuredBase.Contains('#') ||
            configuredBase.Contains('\\') ||
            !Uri.TryCreate(configuredBase, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("https" or "http") ||
            baseUri.UserInfo.Length != 0 ||
            form is null ||
            string.IsNullOrWhiteSpace(form.DocumentationPath))
            return null;

        var path = form.DocumentationPath;
        if (path.StartsWith("//", StringComparison.Ordinal))
            return null;

        var segments = path.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (segments.Any(static segment => segment is "" or "." or ".." ||
            segment.Contains('/') || segment.Contains('\\') ||
            segment.Any(char.IsControl)))
            return null;

        if (!configuredBase.Contains('?'))
            return baseUri.AbsoluteUri.TrimEnd('/') + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));

        var query = baseUri.Query;
        if (query.Length < 3)
            return null;

        var parameterStart = Math.Max(query.LastIndexOf('&'), 0) + 1;
        var assignment = query[parameterStart..];
        if (assignment.Length < 2 || assignment.IndexOf('=') != assignment.Length - 1)
            return null;

        return baseUri.AbsoluteUri + System.Net.WebUtility.UrlEncode(string.Join('/', segments));
    }
}
