using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Сериализация/десериализация дерева фильтра в URL-safe строку
/// для персистенции состояния фильтра в query-параметрах.
/// Конвейер: дерево → JSON → Deflate → Base64Url (и обратно).
/// </summary>
public static class ClayFilterUrlHelper
{
    /// <summary>Имя query-параметра в URL.</summary>
    public const string QueryParamName = "filter";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Сжимает дерево фильтра в URL-безопасную строку:
    /// дерево → JSON → DeflateStream → Base64Url.
    /// Возвращает null, если дерево пустое.
    /// </summary>
    public static string? Serialize(ClayFilterGroupNode? root)
    {
        if (root is null || root.Nodes.Count == 0)
            return null;

        var json = JsonSerializer.Serialize<IClayFilterNode>(root, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
            deflate.Write(bytes, 0, bytes.Length);

        return Base64UrlEncode(output.ToArray());
    }

    /// <summary>
    /// Восстанавливает дерево фильтра из URL-безопасной строки:
    /// Base64Url → DeflateStream → JSON → дерево.
    /// Возвращает null при ошибке или пустой строке.
    /// </summary>
    public static ClayFilterGroupNode? Deserialize(string? base64Url)
    {
        if (string.IsNullOrEmpty(base64Url))
            return null;

        try
        {
            var compressed = Base64UrlDecode(base64Url);

            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            var json = reader.ReadToEnd();

            return JsonSerializer.Deserialize<ClayFilterGroupNode>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // ── Base64Url helpers ──────────────────────────────────────────────────

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data)
                  .TrimEnd('=')
                  .Replace('+', '-')
                  .Replace('/', '_');

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var padded = base64Url.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "=";  break;
        }
        return Convert.FromBase64String(padded);
    }
}
