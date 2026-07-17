using System.Net;
using System.Text.RegularExpressions;
using Clayzor.Lib.Entities.DynamicGrid;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Чтение ячейки динамической строки (<see cref="ClayDynamicRow"/>) для печати и Excel.
/// Повторяет семантику cell-шаблонов из ClayGrid.InitDynamicMode: подставляет наименования
/// вместо кодов (Тип 5/9), сдвигает UTC в пояс клиента (Тип 10/13), приводит к тексту то,
/// что на экране является разметкой (Тип 4/8/12).
/// ЧИСТЫЙ класс — БД не трогает, всё приходит через конструктор.
/// </summary>
public sealed partial class ClayDynamicCellReader : IClayGridCellReader
{
    private readonly Dictionary<string, ClayColumnDefinition> _colBySqlName;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _lookups;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string Tooltip, string Href)>> _iconLookups;
    private readonly TimeSpan _clientOffset;

    public ClayDynamicCellReader(
        IEnumerable<ClayColumnDefinition> columns,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> lookups,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string Tooltip, string Href)>> iconLookups,
        TimeSpan clientOffset)
    {
        _colBySqlName = columns.ToDictionary(c => c.Column, StringComparer.OrdinalIgnoreCase);
        _lookups      = lookups;
        _iconLookups  = iconLookups;
        _clientOffset = clientOffset;
    }

    /// <inheritdoc/>
    public bool TryGetCellValue(IDetailRow row, ClayColumnMeta column, out object? value, out Type valueType)
    {
        value     = null;
        valueType = typeof(string);

        if (row.Item is not IReadOnlyDictionary<string, object?> dict) return false;
        if (!dict.TryGetValue(column.SqlName, out var raw)) return false;
        if (raw is DBNull) raw = null;

        if (!_colBySqlName.TryGetValue(column.SqlName, out var def))
        {
            // Колонки нет в определении — отдаём как есть, форматирует генератор.
            value     = raw;
            valueType = raw?.GetType() ?? typeof(string);
            return true;
        }

        switch ((ClayColumnKind)def.Type)
        {
            // ── Тип 5: в данных код, на экране наименование из справочника ──────
            case ClayColumnKind.List:
                value     = ResolveLookup(column.SqlName, raw);
                valueType = typeof(string);
                return true;

            // ── Тип 9: на экране картинка; в тексте — тултип ────────────────────
            case ClayColumnKind.Icon:
                value     = ResolveIconTooltip(column.SqlName, raw);
                valueType = typeof(string);
                return true;

            // ── Тип 10/13: в данных UTC, на экране время клиента ────────────────
            case ClayColumnKind.DateTimeLocal:
            case ClayColumnKind.TimeLocal:
                value     = ClayDateTimeConverter.Format(raw, def.Format, _clientOffset);
                valueType = typeof(string);
                return true;

            // ── Тип 4: на экране гиперссылка; в тексте — подпись ────────────────
            case ClayColumnKind.Link:
                value     = raw?.ToString();
                valueType = typeof(string);
                return true;

            // ── Тип 8: на экране HTML; в тексте — без разметки ──────────────────
            case ClayColumnKind.Html:
                value     = StripHtml(raw?.ToString());
                valueType = typeof(string);
                return true;

            // ── Тип 12: на экране обрезан; в выгрузке — ПОЛНЫЙ текст ────────────
            case ClayColumnKind.LimitedText:
                value     = raw?.ToString();
                valueType = typeof(string);
                return true;

            // ── Тип 1/2/3/7: отдаём как есть, форматирует генератор ─────────────
            default:
                value     = raw;
                valueType = raw?.GetType() ?? typeof(string);
                return true;
        }
    }

    private string? ResolveLookup(string sqlName, object? raw)
    {
        var key = raw?.ToString();
        if (key is null) return null;
        return _lookups.TryGetValue(sqlName, out var map) && map.TryGetValue(key, out var text)
            ? text
            : key;   // кода нет в справочнике → показываем код, как это делает cell-шаблон
    }

    private string? ResolveIconTooltip(string sqlName, object? raw)
    {
        var key = raw?.ToString();
        if (key is null) return null;
        return _iconLookups.TryGetValue(sqlName, out var map) && map.TryGetValue(key, out var data)
            ? data.Tooltip
            : key;
    }

    /// <summary>Снимает HTML-разметку, оставляя текст.</summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;

        var stripped = StripTagsRegex().Replace(html, "");
        return WebUtility.HtmlDecode(stripped);
    }

    [GeneratedRegex("<.*?>")]
    private static partial Regex StripTagsRegex();
}
