using System.Text;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Components.Grid;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>
/// Генератор HTML-документа для печати всех данных грида Clayzor.
/// Генерирует HTML с теми же CSS-классами MudBlazor (mud-table, mud-table-cell, ...)
/// и встраивает полный @media print CSS из app.css, поэтому печатная форма
/// визуально идентична печати текущей страницы.
/// </summary>
public static class ClayGridPrintHtmlGenerator
{
    /// <summary>
    /// Строит HTML печатной формы. Значения ячеек достаёт <paramref name="cellReader"/>.
    /// </summary>
    public static string Build(
        string title,
        IReadOnlyList<ClayColumnMeta> columns,
        IReadOnlyList<IClayGridRow> rows,
        IClayGridCellReader cellReader,
        HashSet<string>? expandedGroups = null,
        string? filterDescription = null,
        string? groupDescription = null)
    {
        int colCount = columns.Count;
        if (colCount == 0) return "<html><body></body></html>";

        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.Append("<style>");
        EmbedStyles(sb);
        sb.Append("</style></head><body>");

        // Обёртка с классом clay-grid-printing — активирует @media print CSS
        sb.Append("<div class=\"clay-grid-printing mud-paper\" style=\"position:relative\">");

        // ── Заголовок ────────────────────────────────────────────────
        sb.Append("<h5 class=\"mud-typography mud-typography-h5\">")
          .Append(EscapeHtml(title)).Append("</h5>");

        // ── Описания группировки и фильтрации ────────────────────────
        if (!string.IsNullOrWhiteSpace(groupDescription) || !string.IsNullOrWhiteSpace(filterDescription))
        {
            sb.Append("<div class=\"clay-grid-print-descriptions\">");
            if (!string.IsNullOrWhiteSpace(groupDescription))
            {
                sb.Append("<div class=\"clay-grid-print-group-desc\">")
                  .Append(EscapeHtml(groupDescription)).Append("</div>");
            }
            if (!string.IsNullOrWhiteSpace(filterDescription))
            {
                sb.Append("<div class=\"clay-grid-print-filter-desc\">")
                  .Append(EscapeHtml(filterDescription)).Append("</div>");
            }
            sb.Append("</div>");
        }

        // ── Таблица в структуре MudBlazor ────────────────────────────
        sb.Append("<div class=\"mud-table-container\">");
        sb.Append("<div class=\"mud-table mud-table-dense\">");
        sb.Append("<table class=\"mud-table-root\">");

        // THEAD
        sb.Append("<thead class=\"mud-table-head\"><tr class=\"mud-table-row\">");
        for (int c = 0; c < colCount; c++)
        {
            sb.Append("<th class=\"mud-table-cell\">")
              .Append(EscapeHtml(columns[c].DisplayName))
              .Append("</th>");
        }
        sb.Append("</tr></thead>");

        // TBODY
        sb.Append("<tbody class=\"mud-table-body\">");
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row is GroupHeaderRow gh)
                AppendGroupRow(sb, gh, colCount);
            else if (row is IDetailRow detailRow)
                AppendDetailRow(sb, detailRow, columns, cellReader);
        }
        sb.Append("</tbody>");

        sb.Append("</table>");
        sb.Append("</div>");
        sb.Append("</div>");

        sb.Append("</div>");
        sb.Append("</body></html>");

        return sb.ToString();
    }

    /// <summary>
    /// Перегрузка для статического режима: читает ячейки рефлексией по <paramref name="entityType"/>.
    /// </summary>
    public static string Build(
        string title,
        IReadOnlyList<ClayColumnMeta> columns,
        IReadOnlyList<IClayGridRow> rows,
        Type entityType,
        HashSet<string>? expandedGroups = null,
        string? filterDescription = null,
        string? groupDescription = null)
        => Build(title, columns, rows, new ClayReflectionCellReader(entityType),
                 expandedGroups, filterDescription, groupDescription);

    // ── Групповая строка ────────────────────────────────────────────────

    private static void AppendGroupRow(StringBuilder sb, GroupHeaderRow gh, int colCount)
    {
        sb.Append("<tr class=\"mud-table-row\" style=\"page-break-inside:avoid\">");
        sb.Append("<td class=\"mud-table-cell group-header-cell\" colspan=\"")
          .Append(colCount).Append("\" style=\"padding-left:")
          .Append(16 + gh.Depth * 16).Append("px\">")
          .Append(EscapeHtml(gh.DisplayValue));
        sb.Append(" (").Append(gh.ItemCount).Append(" шт.)");
        sb.Append("</td>");
        sb.Append("</tr>");
    }

    // ── Строка детализации ─────────────────────────────────────────────

    private static void AppendDetailRow(
        StringBuilder sb, IDetailRow detailRow,
        IReadOnlyList<ClayColumnMeta> columns,
        IClayGridCellReader cellReader)
    {
        if (detailRow.Item is null) return;

        sb.Append("<tr class=\"mud-table-row\" style=\"page-break-inside:avoid\">");
        for (int c = 0; c < columns.Count; c++)
        {
            string cellValue = "";
            if (cellReader.TryGetCellValue(detailRow, columns[c], out var value, out var valueType))
                cellValue = FormatCellValue(value, valueType);

            sb.Append("<td class=\"mud-table-cell\">")
              .Append(EscapeHtml(cellValue))
              .Append("</td>");
        }
        sb.Append("</tr>");
    }

    // ── Форматирование значений ─────────────────────────────────────────

    private static string FormatCellValue(object? value, Type propertyType)
    {
        if (value is null || value == DBNull.Value) return "";
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (type == typeof(bool))
            return (bool)value
                ? ClayGridPrintStyles.BoolTrueIcon
                : ClayGridPrintStyles.BoolFalseIcon;
        if (type == typeof(DateTime))
            return ((DateTime)value).ToString("dd.MM.yyyy");
        if (type == typeof(decimal) || type == typeof(float) || type == typeof(double))
            return ((IFormattable)value).ToString("N2", null);

        return value.ToString() ?? "";
    }

    // ── Встроенный CSS (MudBlazor base + app.css overrides + @media print) ──

    private static void EmbedStyles(StringBuilder sb)
    {
        // CSS-переменные Clayzor
        sb.Append(":root{")
          .Append("--lh-navy:#05164D;--lh-white:#fff;--lh-gold:#FFAD00;--lh-grey-light:#EBEDF0;")
          .Append("--mud-palette-table-lines:#EBEDF0;")
          .Append("}");

        // MudBlazor base table CSS
        sb.Append(".mud-table-root{width:100%;border-spacing:0;font-family:Verdana,Arial,sans-serif;font-size:12pt}")
          .Append(".mud-table-cell{display:table-cell;padding:6px 16px;text-align:start;vertical-align:inherit}")
          .Append(".mud-table-head .mud-table-cell{font-weight:500}")
          .Append(".mud-table-body .mud-table-cell{font-weight:400}")

        // App.css overrides — хедер
          .Append(".mud-table .mud-table-head .mud-table-cell{")
          .Append("background-color:var(--lh-navy)!important;color:var(--lh-white)!important;")
          .Append("font-weight:600;font-size:0.75rem;letter-spacing:0.06em;text-transform:uppercase;")
          .Append("border-bottom:2px solid var(--lh-gold)!important}")

        // App.css overrides — границы ячеек
          .Append(".mud-table-cell{border-bottom:1px solid var(--lh-grey-light)!important;")
          .Append("border-right:1px solid var(--lh-grey-light)!important}")
          .Append(".mud-table-cell:last-child{border-right:none}")

        // App.css overrides — групповые строки
          .Append(".mud-table-row:has(.group-header-cell){background-color:rgba(5,22,77,0.04)!important;")
          .Append("border-top:2px solid var(--lh-navy)!important}")
          .Append(".group-header-cell{font-weight:600;color:var(--lh-navy)}");

        // ════════════════════════════════════════════════════════════
        // @media print — точная копия из app.css
        // ════════════════════════════════════════════════════════════
        sb.Append("@media print{");

        // Скрыть layout chrome
        sb.Append(".mud-appbar,.mud-drawer,.mud-drawer-responsive,.mud-overlay,.mud-popover,.mud-snackbar{display:none!important}");
        sb.Append(".mud-main-content>*:not(.clay-grid-printing){display:none!important}");
        sb.Append(".mud-main-content{padding:0!important;margin:0!important;padding-top:0!important}");
        sb.Append("body{background:#fff!important}");

        // Скрыть тулбар
        sb.Append(".clay-grid-printing .clay-grid-search,");
        sb.Append(".clay-grid-printing .toolbar-columns-btn,.clay-grid-printing .toolbar-add-btn,");
        sb.Append(".clay-grid-printing .toolbar-select-btn,.clay-grid-printing .toolbar-batch-btn,");
        sb.Append(".clay-grid-printing .grouping-toggle-btn,.clay-grid-printing .filter-toggle-btn");
        sb.Append("{display:none!important}");

        // Скрыть пагинатор
        sb.Append(".clay-grid-printing .clay-grid-paginator{display:none!important}");

        // Скрыть чекбоксы
        sb.Append(".clay-grid-printing .mud-table-head .mud-table-cell:has(.mud-checkbox),");
        sb.Append(".clay-grid-printing .mud-table-body .mud-table-cell:has(.mud-checkbox){display:none!important}");

        // Скрыть кнопки на чипах
        sb.Append(".clay-grid-printing .chip-remove-btn,.clay-grid-printing .grouping-tray-add-btn{display:none!important}");

        // Треи скрыты, описания показаны
        sb.Append(".clay-grid-printing .grouping-tray,.clay-grid-printing .filter-tray,");
        sb.Append(".clay-grid-printing .grouping-tray--active,.clay-grid-printing .filter-tray--active{display:none!important}");
        sb.Append(".clay-grid-print-descriptions{display:none}");
        sb.Append(".clay-grid-printing .clay-grid-print-descriptions{display:block!important;margin-bottom:8px!important}");
        sb.Append(".clay-grid-printing .clay-grid-print-group-desc,");
        sb.Append(".clay-grid-printing .clay-grid-print-filter-desc{font-size:8pt!important;color:#555!important;margin-bottom:4px!important}");

        // Заголовок
        sb.Append(".clay-grid-printing .mud-typography-h5{display:block!important;font-size:16pt!important;");
        sb.Append("font-weight:bold!important;color:#000!important;margin-bottom:12px!important}");

        // MudPaper
        sb.Append(".clay-grid-printing.mud-paper{box-shadow:none!important;border:none!important;");
        sb.Append("border-radius:0!important;padding:8px!important}");

        // Контейнер таблицы
        sb.Append(".clay-grid-printing .mud-table-container{overflow:visible!important;");
        sb.Append("height:auto!important;max-height:none!important}");

        // Таблица
        sb.Append(".clay-grid-printing .mud-table{background:#fff!important;width:100%!important}");

        // Хедер — тёмный
        sb.Append(".clay-grid-printing .mud-table-head .mud-table-cell{background:#222!important;");
        sb.Append("color:#fff!important;font-weight:bold!important;border-bottom:2px solid #000!important;");
        sb.Append("-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}");

        // Ячейки данных
        sb.Append(".clay-grid-printing .mud-table-body .mud-table-cell{border-bottom:1px solid #ccc!important;");
        sb.Append("border-right:1px solid #eee!important;color:#000!important;background:#fff!important}");

        // Убрать hover/striped
        sb.Append(".clay-grid-printing .mud-table-body .mud-table-row:hover{background:transparent!important}");
        sb.Append(".clay-grid-printing .mud-table-body .mud-table-row:hover .mud-table-cell{background:#fff!important}");
        sb.Append(".clay-grid-printing .mud-table-striped .mud-table-row:nth-child(even) .mud-table-cell{background:#fff!important}");

        // Групповые строки
        sb.Append(".clay-grid-printing .mud-table-row:has(.group-header-cell){background:#fff!important;");
        sb.Append("border-top:2px solid #000!important}");
        sb.Append(".clay-grid-printing .group-header-cell{color:#000!important;font-weight:bold!important}");

        // Thead на каждой странице
        sb.Append(".clay-grid-printing .mud-table-head{display:table-header-group}");
        sb.Append(".clay-grid-printing .mud-table-row{page-break-inside:avoid}");

        sb.Append("}"); // конец @media print

        // @page
        sb.Append("@page{size:landscape;margin:15mm}");
    }

    // ── HTML-экранирование ──────────────────────────────────────────────

    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
