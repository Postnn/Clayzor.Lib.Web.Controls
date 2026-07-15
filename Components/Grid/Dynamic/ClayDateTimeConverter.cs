using System.Globalization;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Конвертация даты/времени из UTC в локальный часовой пояс.
/// Все методы принимают явное смещение — чистое, тестируемое поведение.
/// </summary>
public static class ClayDateTimeConverter
{
    /// <summary>
    /// Конвертирует UTC-дату в локальную с указанным смещением.
    /// </summary>
    /// <param name="utc">Дата в UTC (может быть null).</param>
    /// <param name="offset">Смещение локального пояса от UTC (напр. +03:00).</param>
    /// <returns>Локальная дата или null.</returns>
    public static DateTime? ConvertFromUtc(DateTime? utc, TimeSpan offset)
    {
        if (utc is null) return null;

        if (utc.Value.Kind == DateTimeKind.Utc)
            return utc.Value + offset;

        // Если Kind не UTC — считаем, что значение уже локальное
        return utc.Value;
    }

    /// <summary>
    /// Форматирует дату с учётом смещения и строки формата.
    /// </summary>
    /// <param name="value">Сырое значение (DateTime или строка).</param>
    /// <param name="format">Строка формата .NET (напр. "dd.MM.yyyy" или "HH:mm").</param>
    /// <param name="offset">Смещение локального пояса (если null — без конвертации).</param>
    /// <returns>Отформатированная строка.</returns>
    public static string Format(object? value, string? format, TimeSpan? offset = null)
    {
        if (value is null) return "";

        DateTime? dt = value switch
        {
            DateTime d => d,
            DateTimeOffset dto => dto.DateTime,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
            _ => null
        };

        if (dt is null) return value.ToString() ?? "";

        var local = offset.HasValue ? ConvertFromUtc(dt, offset.Value) : dt;
        var fmt   = string.IsNullOrWhiteSpace(format) ? "G" : format;

        return local?.ToString(fmt, CultureInfo.InvariantCulture) ?? "";
    }
}
