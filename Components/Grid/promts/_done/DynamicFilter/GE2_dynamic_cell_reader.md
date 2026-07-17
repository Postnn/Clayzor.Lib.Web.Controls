> Часть плана «Печать и Excel динамического грида». Перед началом прочитай **GE0_README_dynamic_export.md** и **_readme_grid_dynamic.md**. Требует выполненного **GE1**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GE2 — `ClayDynamicCellReader`: значение ячейки динамической строки

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/IClayGridCellReader.cs` (создан в GE1) — контракт, особенно семантику
  возвращаемого `false` / `true` + `value = null`.
- `Components/Grid/ClayGrid.Dynamic.cs` — **сборка cell-шаблонов в `InitDynamicMode`, целиком.
  Это эталон: печать и Excel обязаны показывать ТО ЖЕ, что ячейка на экране.** Смотри, что
  делает шаблон для каждого `ClayColumnKind`, откуда берёт `_dynamicLookups`,
  `_dynamicIconLookups`, `_clientOffset`, `col.Format`.
- `Components/Grid/Dynamic/ClayColumnKind.cs` — все 13 типов.
- `Components/Grid/Dynamic/ClayDateTimeConverter.cs` — `ConvertFromUtc`, `Format`.
- `Clayzor.Lib.Entities/DynamicGrid/ClayColumnDefinition.cs` — `Column`, `Type`, `Format`.
- `Components/Grid/Dynamic/ClayDynamicRow.cs` — строка-словарь.
- `Services/ClayGridPrintHtmlGenerator.cs` → `FormatCellValue` и
  `Services/ClayGridExcelGenerator.cs` → `SetCellValue` — **что генератор сделает с тем типом,
  который ты вернёшь.** От этого зависит, что возвращать.
- `Components/Grid/ColumnTypes/ClayLimitedTextColumnType.cs`, `ClayHtmlColumnType.cs` — как
  обрезается текст и санитизируется HTML.

## Задача

Динамическая строка — словарь. Значение достать легко, но отдать «как есть» нельзя: в данных
лежат коды и UTC, а на экране — наименования и локальное время (см. GE0). Печатная форма,
показывающая `3` вместо `Общий анализ крови` и `21:00` вместо `00:00`, хуже пустой: неверные
данные выглядят верными.

Правило шага одно: **`ClayDynamicCellReader` обязан отдавать то же, что показывает cell-шаблон
на экране.** Всё, что ниже, — следствие. Если увидишь расхождение между этим промтом и
фактическим шаблоном в `InitDynamicMode` — прав шаблон; скажи о расхождении и сделай как в нём.

## Изменить/создать

Создать `Components/Grid/Dynamic/ClayDynamicCellReader.cs`:

```csharp
using Clayzor.Lib.Entities.DynamicGrid;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Чтение ячейки динамической строки (<see cref="ClayDynamicRow"/>) для печати и Excel.
/// Повторяет семантику cell-шаблонов из ClayGrid.InitDynamicMode: подставляет наименования
/// вместо кодов (Тип 5/9), сдвигает UTC в пояс клиента (Тип 10/13), приводит к тексту то,
/// что на экране является разметкой (Тип 4/8/12).
/// ЧИСТЫЙ класс — БД не трогает, всё приходит через конструктор.
/// </summary>
public sealed class ClayDynamicCellReader : IClayGridCellReader
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
}
```

Разбор решений — **прочитай, прежде чем «улучшать»**:

- **`valueType = raw?.GetType()`, а не тип свойства.** У словаря нет свойств; тип берётся у
  значения. Следствие, с которым надо смириться: для `NULL` в числовой колонке тип неизвестен,
  вернётся `typeof(string)`. Генератор при `value == null` всё равно ничего не пишет
  (`if (value is null || value == DBNull.Value) return;` в `SetCellValue`), так что вреда нет.
- **Тип 1/3/7 (число, дата, булево) отдаём СЫРЫМИ**, не строкой. Это принципиально для Excel:
  `SetCellValue` по `typeof(int)` положит число с выравниванием вправо, по `typeof(DateTime)` —
  дату с форматом `dd.MM.yyyy`, по `typeof(bool)` — «Да»/«Нет». Вернёшь строку — в Excel будет
  текст, по нему не отсортировать и не посчитать сумму. **Это главная ошибка, которую здесь
  легко сделать.**
- **Тип 10/13 отдаём СТРОКОЙ.** Обидно (в Excel будет текст, а не дата), но иначе никак:
  `ClayDateTimeConverter.Format` применяет `def.Format` — пользовательский формат из справочника,
  и результат может быть каким угодно (`HH:mm`, `dd.MM.yy HH:mm` и т.п.). Отдавать сдвинутый
  `DateTime` и терять формат — хуже. Зафиксируй это в отчёте как известный компромисс.
- **Тип 12 отдаёт ПОЛНЫЙ текст, а не обрезанный.** Обрезка «…» на экране — способ уместить
  текст в ячейку. В печати и Excel места больше, и терять данные при выгрузке нельзя. Это
  сознательное отличие от экрана — единственное в этом классе.
- **Тип 4 (Ссылка)** — в печати ссылка кликом всё равно не работает, в Excel гиперссылки не
  делаем. Отдаём подпись текстом.
- **Тип 6/11 (фильтр-онли)** сюда не попадут: их нет в колонках вывода. Ветки для них не пиши.
- **`StringComparer.OrdinalIgnoreCase`** в `_colBySqlName` — как в `ClayReflectionCellReader`.
  А вот `dict.TryGetValue` регистрозависим — ключи словаря приходят из SQL и совпадают с
  `SqlName` точно. Не «чини» это.

`StripHtml` — приватный статический метод. Если в `ClayHtmlColumnType` уже есть готовая
функция снятия разметки — **переиспользуй её, не пиши вторую**. Если нет — простейшая
реализация через `Regex.Replace(s, "<.*?>", "")` + `WebUtility.HtmlDecode`. Не тащи сюда
HtmlAgilityPack.

## Не делай

Не обращайся к БД и не инжектируй `DbManager` — всё приходит конструктором. Справочники уже
загружены один раз в `InitDynamicMode`, повторно их грузить нельзя. Не трогай генераторы
(GE1 их уже подготовил). Не трогай `ClayDateTimeConverter`. Не создавай экземпляр читателя в
гриде — это GE4/GE5. Не меняй cell-шаблоны в `InitDynamicMode`: эталон — они, ты подстраиваешься
под них.

## Проверка (юнит-тесты, БД не нужна)

Новый файл в проекте тестов Controls. Строка — `new ClayDynamicRow(new Dictionary<string, object?> {...})`,
колонка — `new ClayColumnMeta { SqlName = "...", Type = ... }`.

- **Тип 2 (Текст)**: `{"Название": "Гемоглобин"}` → `true`, `value == "Гемоглобин"`,
  `valueType == typeof(string)`;
- **Тип 1 (Число)**: `{"Кол": 42}` → `value` это `int` `42`, **`valueType == typeof(int)`**,
  а не `typeof(string)`;
- **Тип 3 (Дата)**: `{"Создано": new DateTime(2026,1,15)}` → `valueType == typeof(DateTime)`,
  значение не изменено;
- **Тип 7 (Булево)**: `{"Активно": true}` → `valueType == typeof(bool)`;
- **Тип 5 (Список)**, справочник `{"3": "Общий анализ крови"}`: `{"КодТипа": 3}` →
  `value == "Общий анализ крови"`, `valueType == typeof(string)`;
- Тип 5, кода нет в справочнике: `{"КодТипа": 99}` → `value == "99"` (код как есть, не пусто);
- Тип 5, `{"КодТипа": null}` → `true`, `value == null`;
- **Тип 9 (Пиктограмма)**, справочник `{"1": ("Готов", "/img/ok.png")}`: `{"Статус": 1}` →
  `value == "Готов"` (тултип, не href);
- **Тип 10**, `clientOffset = +3ч`, `{"Изменено": new DateTime(2026,1,1,21,0,0, DateTimeKind.Utc)}`,
  `Format = "dd.MM.yyyy HH:mm"` → `value == "02.01.2026 00:00"`, `valueType == typeof(string)`;
- **Тип 13**, `Format = "HH:mm"` → то же со временем;
- **Тип 8 (HTML)**: `{"Описание": "<b>Важно</b> &amp; срочно"}` → `value == "Важно & срочно"`;
- **Тип 12 (LimitedText)**, `Format = "20"`, текст на 100 символов → `value` — **полный текст
  без «…»** (сознательное отличие от экрана);
- **`DBNull.Value`** в любой колонке → `true`, `value == null`;
- **колонки нет в словаре** → `false`;
- **`row.Item` не словарь** (например, `DetailRow<MedicalTest>`) → `false`, не бросает;
- **колонки нет в `_dynamicCols`**, но есть в словаре → `true`, значение как есть;
- регистр `SqlName` в `_colBySqlName` не важен.

`dotnet build` зелёный. Поведение обоих режимов не изменилось — класс пока никем не создаётся.
