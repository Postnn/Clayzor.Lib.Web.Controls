namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Достаёт значение ячейки из строки детализации для печати и выгрузки в Excel.
/// Генераторы не знают, чем является строка — сущностью со свойствами или словарём, —
/// и получают значение вместе с CLR-типом, по которому его форматировать.
/// </summary>
public interface IClayGridCellReader
{
    /// <summary>
    /// Значение ячейки колонки <paramref name="column"/> в строке <paramref name="row"/>.
    /// </summary>
    /// <param name="row">Строка детализации (не заголовок группы).</param>
    /// <param name="column">Колонка вывода.</param>
    /// <param name="value">
    /// Значение. null — пустая ячейка. Уже приведено к тому виду, в котором его надо показать
    /// (реализация может подменить код на наименование, сдвинуть время и т.п.).
    /// </param>
    /// <param name="valueType">
    /// CLR-тип для форматирования: bool → «Да»/«Нет», DateTime → dd.MM.yyyy, числа → выравнивание
    /// вправо и числовой формат Excel, остальное → строка. Может быть Nullable&lt;&gt;.
    /// </param>
    /// <returns>
    /// false — колонки нет в строке (ячейка не заполняется вообще).
    /// true с value = null — колонка есть, значение пустое.
    /// </returns>
    bool TryGetCellValue(IDetailRow row, ClayColumnMeta column, out object? value, out Type valueType);
}
