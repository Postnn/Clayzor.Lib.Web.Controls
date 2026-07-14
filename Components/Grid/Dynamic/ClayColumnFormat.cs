namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Разбор строки формата колонки из БД (колонка <c>Формат</c> в ClayGridColumns).
/// </summary>
public static class ClayColumnFormat
{
    /// <summary>
    /// Разбирает строку формата в зависимости от типа колонки.
    /// Для Number/Date/Bool — возвращает формат как есть (используется при рендере).
    /// Для null/пусто — возвращает как есть.
    /// </summary>
    /// <param name="clayColumnKind">Тип колонки (1–13).</param>
    /// <param name="format">Строка формата из БД.</param>
    /// <returns>Строка формата или null.</returns>
    public static string? Parse(int clayColumnKind, string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return format;

        return clayColumnKind switch
        {
            (int)ClayColumnKind.Number => format, // "N2", "C2", etc.
            (int)ClayColumnKind.Date   => format, // "dd.MM.yyyy", etc.
            (int)ClayColumnKind.Bool   => format, // "Активно=1"
            _ => format
        };
    }
}
