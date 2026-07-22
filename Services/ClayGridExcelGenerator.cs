using ClosedXML.Excel;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Components.Grid;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>
/// Генератор Excel-файлов (.xlsx) для данных грида Clayzor.
/// Использует ClosedXML для создания стилизованных книг с цветовой гаммой Clayzor.
/// </summary>
public static class ClayGridExcelGenerator
{
    // ── Цветовая гамма и типографика Clayzor ────────────────────────────────────
    private const string ClayFontFamily = "Verdana";

    private static readonly XLColor ClayNavy  = XLColor.FromHtml("#05164D");
    private static readonly XLColor ClayGold  = XLColor.FromHtml("#FFAD00");
    private static readonly XLColor StripeGray = XLColor.FromHtml("#F2F4F7");
    private static readonly XLColor BorderGray = XLColor.FromHtml("#D0D0D0");

    /// <summary>
    /// Генерирует .xlsx файл и возвращает его как массив байт.
    /// Значения ячеек достаёт <paramref name="cellReader"/>.
    /// </summary>
    /// <param name="title">Заголовок грида (первая строка Excel).</param>
    /// <param name="columns">Видимые колонки в порядке отображения.</param>
    /// <param name="rows">Строки данных (заголовки групп + строки детализации).</param>
    /// <param name="cellReader">Читатель значений ячеек из строк детализации.</param>
    /// <param name="expandedGroups">FullKey развёрнутых групп. Группы, которых нет в наборе, будут свёрнуты в Excel Outline.</param>
    /// <param name="filterDescription">Текстовое описание активных фильтров (или null).</param>
    /// <param name="groupDescription">Текстовое описание колонок группировки (или null).</param>
    public static byte[] ExportToExcel(
        string title,
        IReadOnlyList<ClayColumnMeta> columns,
        IReadOnlyList<IClayGridRow> rows,
        IClayGridCellReader cellReader,
        HashSet<string>? expandedGroups = null,
        string? filterDescription = null,
        string? groupDescription = null)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Данные");

        // Кнопки +/- сверху группы (на строке заголовка)
        ws.Outline.SummaryVLocation = XLOutlineSummaryVLocation.Top;

        int colCount = columns.Count;
        if (colCount == 0) return Array.Empty<byte>();

        // ── Строка 1: Заголовок ────────────────────────────────────────
        WriteTitleRow(ws, 1, title, colCount);

        int currentRow = 2;

        // ── Строка 2 (опционально): описание группировки ──────────────
        if (!string.IsNullOrWhiteSpace(groupDescription))
        {
            WriteInfoRow(ws, currentRow, groupDescription, colCount);
            currentRow++;
        }

        // ── Строка 3 (опционально): описание фильтра ──────────────────
        if (!string.IsNullOrWhiteSpace(filterDescription))
        {
            WriteInfoRow(ws, currentRow, filterDescription, colCount);
            currentRow++;
        }

        // ── Заголовки колонок ──────────────────────────────────────────
        WriteHeaderRow(ws, currentRow, columns);
        currentRow++;

        // ── Стек групп для вложенных Excel Outline ──────────────────
        // (HeaderRow, Depth, DataStart, IsExpanded)
        var groupStack = new Stack<(int HeaderRow, int Depth, int DataStart, bool IsExpanded)>();

        foreach (var row in rows)
        {
            if (row is GroupHeaderRow gh)
            {
                // Закрываем группы, глубина которых >= глубине нового заголовка
                while (groupStack.Count > 0 && groupStack.Peek().Depth >= gh.Depth)
                {
                    var closed = groupStack.Pop();
                    int dEnd = currentRow - 1;
                    if (closed.DataStart > 0 && dEnd >= closed.DataStart)
                    {
                        ws.Rows(closed.DataStart, dEnd).Group();
                        if (!closed.IsExpanded)
                            ws.Rows(closed.DataStart, dEnd).Collapse();
                    }
                }

                // Если есть родительская группа без DataStart — начинаем её диапазон
                if (groupStack.Count > 0)
                {
                    var parent = groupStack.Pop();
                    if (parent.DataStart == 0)
                        parent.DataStart = currentRow;
                    groupStack.Push(parent);
                }

                WriteGroupHeaderRow(ws, currentRow, gh, colCount);
                groupStack.Push((currentRow, gh.Depth, 0, expandedGroups?.Contains(gh.FullKey) ?? true));
            }
            else if (row is IDetailRow detailRow)
            {
                // Фиксируем начало данных для группы на вершине стека
                if (groupStack.Count > 0)
                {
                    var top = groupStack.Pop();
                    if (top.DataStart == 0)
                        top.DataStart = currentRow;
                    groupStack.Push(top);
                }
                WriteDetailRow(ws, currentRow, detailRow, columns, cellReader);
            }
            currentRow++;
        }

        // Закрыть оставшиеся группы (от глубоких к внешним)
        while (groupStack.Count > 0)
        {
            var closed = groupStack.Pop();
            int dEnd = currentRow - 1;
            if (closed.DataStart > 0 && dEnd >= closed.DataStart)
            {
                ws.Rows(closed.DataStart, dEnd).Group();
                if (!closed.IsExpanded)
                    ws.Rows(closed.DataStart, dEnd).Collapse();
            }
        }

        // ── Авто-ширина колонок ──────────────────────────────────────
        ws.Columns().AdjustToContents(1, 60);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Перегрузка для статического режима: читает ячейки рефлексией по <paramref name="entityType"/>.
    /// </summary>
    public static byte[] ExportToExcel(
        string title,
        IReadOnlyList<ClayColumnMeta> columns,
        IReadOnlyList<IClayGridRow> rows,
        Type entityType,
        HashSet<string>? expandedGroups = null,
        string? filterDescription = null,
        string? groupDescription = null)
        => ExportToExcel(title, columns, rows, new ClayReflectionCellReader(entityType),
                        expandedGroups, filterDescription, groupDescription);

    // ── Строка заголовка ────────────────────────────────────────────────────

    private static void WriteTitleRow(IXLWorksheet ws, int rowNum, string title, int colCount)
    {
        var row = ws.Row(rowNum);
        row.Height = 32;

        var range = ws.Range(rowNum, 1, rowNum, colCount);
        range.Merge();
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 14;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.FontName = ClayFontFamily;
        range.Style.Fill.BackgroundColor = ClayNavy;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        // Золотое подчёркивание
        range.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        range.Style.Border.BottomBorderColor = ClayGold;

        ws.Cell(rowNum, 1).Value = title;
    }

    // ── Строка с инфо-описанием (фильтр / группировка) ───────────────────────

    private static void WriteInfoRow(IXLWorksheet ws, int rowNum, string description, int colCount)
    {
        var row = ws.Row(rowNum);
        row.Height = 20;

        var range = ws.Range(rowNum, 1, rowNum, colCount);
        range.Merge();
        range.Style.Font.FontSize = 9;
        range.Style.Font.FontName = ClayFontFamily;
        range.Style.Font.FontColor = XLColor.FromHtml("#555555");
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.Indent = 1;

        ws.Cell(rowNum, 1).Value = description;
    }

    // ── Строка заголовков колонок ─────────────────────────────────────────────

    private static void WriteHeaderRow(IXLWorksheet ws, int rowNum, IReadOnlyList<ClayColumnMeta> columns)
    {
        var row = ws.Row(rowNum);
        row.Height = 24;

        for (int c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(rowNum, c + 1);
            cell.Value = columns[c].DisplayName;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Font.FontSize = 9;
            cell.Style.Font.FontName = ClayFontFamily;
            cell.Style.Fill.BackgroundColor = ClayNavy;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.TopBorderColor = BorderGray;
            cell.Style.Border.BottomBorderColor = BorderGray;
            cell.Style.Border.LeftBorderColor = BorderGray;
            cell.Style.Border.RightBorderColor = BorderGray;
            // Wrap text for long headers
            cell.Style.Alignment.WrapText = true;
        }
    }

    // ── Строка заголовка группы ──────────────────────────────────────────────

    private static void WriteGroupHeaderRow(IXLWorksheet ws, int rowNum, GroupHeaderRow header, int colCount)
    {
        var row = ws.Row(rowNum);
        row.Height = 22;

        // Значение + количество в первой колонке
        var cell = ws.Cell(rowNum, 1);
        if (header.SelectedItemCount.HasValue)
        {
            // Режим «выбранные»: показываем количество выбранных
            if (header.SelectedItemCount.Value > 0 && header.SelectedItemCount.Value < header.ItemCount)
                cell.Value = $"{header.DisplayValue} ({header.SelectedItemCount.Value} из {header.ItemCount} шт.)";
            else
                cell.Value = $"{header.DisplayValue} ({header.ItemCount} шт.)";
        }
        else
        {
            cell.Value = $"{header.DisplayValue} ({header.ItemCount} шт.)";
        }
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 10;
        cell.Style.Font.FontName = ClayFontFamily;
        cell.Style.Font.FontColor = ClayNavy;
        cell.Style.Alignment.Indent = header.Depth;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // Стиль всех ячеек строки
        for (int c = 0; c < colCount; c++)
        {
            var ccell = ws.Cell(rowNum, c + 1);
            ccell.Style.Fill.BackgroundColor = StripeGray;
            ccell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            ccell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ccell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            ccell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            ccell.Style.Border.TopBorderColor = BorderGray;
            ccell.Style.Border.BottomBorderColor = BorderGray;
            ccell.Style.Border.LeftBorderColor = BorderGray;
            ccell.Style.Border.RightBorderColor = BorderGray;
        }

        // Объединить оставшиеся колонки для чистого вида
        if (colCount > 1)
        {
            var restRange = ws.Range(rowNum, 2, rowNum, colCount);
            restRange.Merge();
        }
    }

    // ── Строка детализации ───────────────────────────────────────────────────

    private static void WriteDetailRow(
        IXLWorksheet ws, int rowNum, IDetailRow detailRow,
        IReadOnlyList<ClayColumnMeta> columns,
        IClayGridCellReader cellReader)
    {
        if (detailRow.Item is null) return;

        var row = ws.Row(rowNum);
        row.Height = 20;

        bool isEven = rowNum % 2 == 0;

        for (int c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(rowNum, c + 1);

            if (cellReader.TryGetCellValue(detailRow, columns[c], out var value, out var valueType))
                SetCellValue(cell, value, valueType);

            // Стиль ячейки
            cell.Style.Font.FontSize = 9;
            cell.Style.Font.FontName = ClayFontFamily;
            cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.TopBorderColor = BorderGray;
            cell.Style.Border.BottomBorderColor = BorderGray;
            cell.Style.Border.LeftBorderColor = BorderGray;
            cell.Style.Border.RightBorderColor = BorderGray;

            if (isEven)
                cell.Style.Fill.BackgroundColor = StripeGray;
        }
    }

    // ── Установка значения с типизацией ──────────────────────────────────────

    private static void SetCellValue(IXLCell cell, object? value, Type propertyType)
    {
        if (value is null || value == DBNull.Value)
            return;

        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (type == typeof(bool))
        {
            cell.Value = (bool)value ? "Да" : "Нет";
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        else if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
        {
            cell.Value = Convert.ToInt64(value);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }
        else if (type == typeof(decimal) || type == typeof(float) || type == typeof(double))
        {
            cell.Value = Convert.ToDouble(value);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            cell.Style.NumberFormat.Format = "#,##0.##";
        }
        else if (type == typeof(DateTime))
        {
            cell.Value = (DateTime)value;
            cell.Style.NumberFormat.Format = "dd.MM.yyyy";
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        else
        {
            cell.Value = value.ToString() ?? "";
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }
    }
}
