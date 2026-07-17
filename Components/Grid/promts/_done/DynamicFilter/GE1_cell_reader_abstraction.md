> Часть плана «Печать и Excel динамического грида». Перед началом прочитай **GE0_README_dynamic_export.md** и **_readme_grid_dynamic.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GE1 — `IClayGridCellReader`: вынуть чтение ячейки из генераторов

**Самый опасный шаг плана.** Ты трогаешь код, которым прямо сейчас пользуется работающий
статический режим — печать и Excel в `MedicalTests`. Задача шага: вынести способ получения
значения ячейки в абстракцию, **не изменив поведение статического режима ни на байт**.
Никакой динамики здесь нет.

Прочитать перед началом (обязательно, до написания кода):

- `Services/ClayGridPrintHtmlGenerator.cs` — целиком. Особенно `Build`, `AppendDetailRow`,
  `FormatCellValue`, `BuildPropertyMap`.
- `Services/ClayGridExcelGenerator.cs` — целиком. Особенно `ExportToExcel`, `WriteDetailRow`,
  `SetCellValue`, `BuildPropertyMap`.
- `Components/Grid/ClayGridRow.cs` — `IDetailRow.Item`, `IClayGridRow`, `GroupHeaderRow`.
- `Components/Grid/IClayGrid.cs` — `ClayColumnMeta` (обрати внимание: там НЕТ ни `Формат`,
  ни `ClayColumnKind` — только `SqlName`, `DisplayName`, `Type` (дескриптор)).
- Все вызовы генераторов: `ClayGridPageBase.cs` (`BuildPrintHtmlAsync`,
  `BuildPrintHtmlForCurrentPageAsync`), `ClayGridPageBase.Export.Excel.cs` (`ExcelExportAsync`),
  `ClayGridPageBase.Export.Selected.cs` (`BuildPrintHtmlForSelectedAsync`). **Найди их все —
  их четыре.**

## Задача

Оба генератора устроены одинаково:

```csharp
var propMap = BuildPropertyMap(entityType);      // рефлексия по [Column]
...
if (propMap.TryGetValue(sqlName, out var prop))
{
    var value = prop.GetValue(entity);
    SetCellValue(cell, value, prop.PropertyType);   // или FormatCellValue(...)
}
```

Две операции слиты в одну: **достать значение** (рефлексия по `T`) и **отформатировать его**
(по CLR-типу). Динамическая строка — словарь, у неё нет свойств; `BuildPropertyMap` вернёт
пустую карту, и все ячейки будут пустыми (см. GE0).

Разделяем: «достать значение + его тип» уходит в интерфейс, «отформатировать по типу» остаётся
в генераторе как есть.

## Изменить/создать

**1.** Создать `Components/Grid/IClayGridCellReader.cs`:

```csharp
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
```

**2.** Создать `Services/ClayReflectionCellReader.cs` — существующая логика, слово в слово:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Clayzor.Lib.Web.Controls.Components.Grid;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>
/// Чтение ячейки через рефлексию по типу сущности: SqlName → свойство с [Column("SqlName")]
/// (или совпадающим именем). Реализация для статического режима — поведение до GE1.
/// </summary>
public sealed class ClayReflectionCellReader : IClayGridCellReader
{
    private readonly Dictionary<string, PropertyInfo> _propMap;

    public ClayReflectionCellReader(Type entityType)
    {
        _propMap = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            _propMap[colAttr?.Name ?? prop.Name] = prop;
        }
    }

    /// <inheritdoc/>
    public bool TryGetCellValue(IDetailRow row, ClayColumnMeta column, out object? value, out Type valueType)
    {
        value     = null;
        valueType = typeof(string);

        var entity = row.Item;
        if (entity is null) return false;
        if (!_propMap.TryGetValue(column.SqlName, out var prop)) return false;

        value     = prop.GetValue(entity);
        valueType = prop.PropertyType;
        return true;
    }
}
```

`StringComparer.OrdinalIgnoreCase` — не отсебятина, так в текущем `BuildPropertyMap`. Сверься
с оригиналом и повтори точно.

**3.** `Services/ClayGridPrintHtmlGenerator.cs` — новая перегрузка + старая через неё:

```csharp
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
        /* … тело текущего Build, но:
           — строка «var propMap = BuildPropertyMap(entityType);» УДАЛЕНА;
           — AppendDetailRow принимает cellReader вместо propMap … */
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
```

`AppendDetailRow`:

```csharp
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
```

`FormatCellValue`, `AppendGroupRow`, `EmbedStyles`, `EscapeHtml` — **не трогать**.
`BuildPropertyMap` из генератора удалить (логика уехала в `ClayReflectionCellReader`).

**4.** `Services/ClayGridExcelGenerator.cs` — ровно то же самое: перегрузка
`ExportToExcel(..., IClayGridCellReader cellReader, ...)`, старая сигнатура с `Type entityType`
делегирует в неё через `new ClayReflectionCellReader(entityType)`, `WriteDetailRow` принимает
`cellReader`:

```csharp
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

            /* … стилизация ячейки — БЕЗ ИЗМЕНЕНИЙ … */
        }
    }
```

`SetCellValue`, `WriteTitleRow`, `WriteInfoRow`, `WriteHeaderRow`, `WriteGroupHeaderRow`,
логика Excel Outline (`groupStack`) — **не трогать**. `BuildPropertyMap` из генератора удалить.

**5.** Четыре вызова в `ClayGridPageBase*` — **не трогать**. Они передают `typeof(T)` и
попадут в перегрузку-обёртку. Это и есть доказательство, что статический режим не изменился.

## Не делай

Не меняй `FormatCellValue`, `SetCellValue` и вообще ничего в форматировании — это отдельная
ответственность, она остаётся в генераторах. Не меняй вёрстку печати, встроенный CSS, стили
ClosedXML, Excel Outline. Не удаляй перегрузки с `Type entityType` — они публичный API
библиотеки и используются четырьмя местами. Не трогай `ClayGridPageBase*`. Не трогай
`ClayGrid.ExportMenu.cs`. Никакой динамики — `ClayDynamicCellReader` это GE2.

## Проверка

**Юнит (новый файл в проекте тестов Controls):**

- `ClayReflectionCellReader(typeof(MedicalTest))`, строка `new DetailRow<MedicalTest> { Item = entity }`:
  `TryGetCellValue` по колонке с `SqlName = "НазваниеАнализа"` → `true`, значение = значение
  свойства, `valueType == typeof(string)`;
- колонка с `SqlName`, которому не соответствует ни одно свойство → `false`, `value == null`;
- `row.Item == null` → `false`;
- свойство типа `int?` со значением `null` → `true`, `value == null`,
  `valueType == typeof(int?)` (тип берётся у свойства, а не у значения!);
- регистр `SqlName` не важен: `"названиеанализа"` находит то же свойство;
- свойство БЕЗ атрибута `[Column]` находится по имени свойства.

**Ручная — вся про отсутствие регрессии в статике** (`MedicalTests.razor`):

- «Печать → Текущая страница» → форма открывается, **все ячейки заполнены**, значения и
  форматы (даты `dd.MM.yyyy`, дробные `N2`, булевы иконки) точно те же, что до GE1;
- «Печать → Все данные» — то же;
- сгруппировать по типу, раскрыть группу, «Печать → Текущая страница» → строки групп на месте,
  отступы по глубине, счётчики «(N шт.)»;
- выбрать записи, «Печать → Выбранные (N)» → только они;
- «Excel → Текущая страница» → файл скачался, откройте: заголовок, шапка, полосатые строки,
  **числа остались числами** (выравнивание вправо, формат `#,##0.##`), **даты остались датами**
  (формат `dd.MM.yyyy`), булевы — «Да»/«Нет» по центру;
- «Excel → Все данные» с группировкой → сворачивание групп (Outline) работает как раньше;
- «Excel → Выбранные (N)» — то же.

**Самый надёжный способ проверки: до изменений сохрани эталонные файлы** (распечатай в PDF,
скачай xlsx), после — сравни. Если хоть одна ячейка разошлась — ты что-то сломал, откатывайся
и разбирайся, а не «дорабатывай».

Динамический режим на этом шаге не меняется никак: печать и Excel там по-прежнему скрыты (GF15).
