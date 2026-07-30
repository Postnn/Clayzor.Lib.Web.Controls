> Часть серии **CF**. Прочитать `CF0_README_extract_filter.md` и отчёт **CF_A**.
> Делать ТОЛЬКО этот шаг.

# CF_B — тип `ClayFilterColumnInfo` и перевод диалогов фильтра на него

Развязываем контракт фильтра от грида **до** физического переноса. После шага диалоги фильтра
принимают лёгкий `ClayFilterColumnInfo` вместо гридового `ClayColumnMeta`; грид маппит одно
в другое в одном месте. Файлы пока остаются в `Components/Grid/Filter/` — перенос это CF_C.

**Терминология:** `ClayFilterColumnInfo` — **описание фильтруемого поля** (какая колонка, как
называется, какого типа, есть ли справочник значений). Это НЕ фильтр. Фильтр — дерево
`ClayFilterGroupNode`, редактируется `ClayFilterDialog`.

## Прочитать

- отчёт **CF_A** — точный состав полей, которые фильтр реально читает у `ClayColumnMeta`;
- `Components/Grid/IClayGrid.cs` — `ClayColumnMeta`;
- `Components/Grid/Filter/ClayFilterDialog.*`, `ClayFilterGroup.*`, `ClayFilterExpression.*` —
  все `[Parameter] IReadOnlyList<ClayColumnMeta> Columns`;
- `Components/Grid/ClayColumnFilterDialog.*`, `ClayColumnValueFilterDialog.*` — что они читают
  у метаданных (эти диалоги остаются в гриде, но тоже используют тип колонки для фильтра);
- `Components/Grid/ClayGrid.Filtering.cs` — где грид строит списки колонок для диалогов.

## 1. Новый тип

Файл `Components/Grid/Filter/ClayFilterColumnInfo.cs` (в CF_C переедет вместе с папкой),
неймспейс пока `...Components.Grid.Filter`.

```csharp
/// <summary>
/// Описание одного поля, доступного для фильтрации. НЕ фильтр, а вход для диалогов фильтра:
/// из списка таких описаний диалог знает, какие колонки предлагать, какого они типа и есть ли
/// у них справочник значений. Не зависит от грида — пригоден и для дерева.
/// </summary>
public sealed class ClayFilterColumnInfo
{
    /// <summary>SQL-имя колонки — попадает в условие фильтра.</summary>
    public required string SqlName { get; init; }

    /// <summary>Отображаемое имя колонки (в диалоге, чипах, тексте фильтра).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Дескриптор типа — определяет операторы и редактор значения.</summary>
    public required ColumnTypeDescriptor Type { get; init; }

    /// <summary>Справочник значений для выпадающего выбора; null — типозависимый редактор.</summary>
    public IReadOnlyList<ClayFilterOption>? Options { get; init; }

    /// <summary>Подпись значения true булевой колонки; null — «Да».</summary>
    public string? BoolTrueLabel { get; init; }

    /// <summary>Подпись значения false булевой колонки; null — «Нет».</summary>
    public string? BoolFalseLabel { get; init; }
}
```

Состав полей — **по отчёту CF_A**, не по этому черновику: если CF_A показал, что читается
что-то ещё (или `BoolTrue/FalseLabel` не нужны диалогу составного фильтра) — привести в
соответствие и отметить расхождение в отчёте. Ничего «про запас» не добавлять.

`ColumnTypeDescriptor`, `ClayFilterOption` — из текущих мест (в CF_C, возможно, переедут;
здесь используем как есть по их текущему неймспейсу).

## 2. Перевод диалогов и рекурсивных компонентов

В `ClayFilterDialog`, `ClayFilterGroup`, `ClayFilterExpression` заменить параметр
`IReadOnlyList<ClayColumnMeta> Columns` на `IReadOnlyList<ClayFilterColumnInfo> Columns`.
Внутри — все обращения (`c.SqlName`, `c.DisplayName`, `c.Type`, lookup, bool-labels) уже
совпадают по именам с новым типом, менять логику не требуется. Где читался
`ClayColumnMeta`-специфичный член (если CF_A такой нашёл) — решить точечно (перенести в
`ClayFilterColumnInfo` либо убрать зависимость).

`ClayColumnFilterDialog` / `ClayColumnValueFilterDialog` (остаются в гриде): если они принимают
`ClayColumnMeta` — **их не трогаем** в этом шаге, они гридовые и метаданные грида им доступны.
Переводить на `ClayFilterColumnInfo` их не нужно (у них одна колонка, не список).

## 3. Маппер в гриде

В `ClayGrid.Filtering.cs` (или где грид собирает список колонок для `ClayFilterDialog`) —
приватный хелпер:

```csharp
/// <summary>Проецирует метаданные колонки грида в описание фильтруемого поля для диалога.</summary>
private static ClayFilterColumnInfo ToFilterColumn(ClayColumnMeta m) => new()
{
    SqlName        = m.SqlName,
    DisplayName    = m.DisplayName,
    Type           = m.Type,
    BoolTrueLabel  = m.BoolTrueLabel,
    BoolFalseLabel = m.BoolFalseLabel,
    // Options — если грид передаёт lookup в диалог, взять из текущего источника (FilterLookupOptions)
};
```

Место, где строился `filterableCols` (`_columnBySqlName.Values.Where(c => c.Filterable)...`),
теперь заканчивается `.Select(ToFilterColumn)` и отдаёт `IReadOnlyList<ClayFilterColumnInfo>`.
lookup (`_opt.FilterLookupOptions`) грид передаёт диалогу как и раньше — **если** в текущем коде
он идёт отдельным параметром `LookupOptions`, оставить отдельным параметром (не переносить в
`ClayFilterColumnInfo.Options` в этом шаге, чтобы не менять поведение; перенос lookup внутрь
типа — потенциальное улучшение, не рефакторинг).

## Не делай

- Не переноси файлы и не меняй неймспейсы (CF_C).
- Не трогай `ClayColumnMeta` (остаётся гридовым) и не удаляй из него поля.
- Не переводи `ClayColumnFilterDialog`/`ClayColumnValueFilterDialog` на новый тип.
- Не меняй поведение: список колонок в диалоге, сортировка по алфавиту, lookup — как были.
- Не добавляй в `ClayFilterColumnInfo` полей сверх реально читаемых (по CF_A).

## Проверка

- `dotnet build` + `dotnet test` — зелёные;
- `grep -rn "IReadOnlyList<ClayColumnMeta> Columns" src/Clayzor.Lib.Web.Controls/Components/Grid/Filter/`
  → пусто (диалоги переведены);
- `grep -rn "ClayColumnMeta" src/Clayzor.Lib.Web.Controls/Components/Grid/Filter/` → осталось
  только то, что CF_A признал неизбежным (в идеале — пусто: это и есть развязка контракта);
- ручной чек-лист «Статический грид» из CF0: настраиваемый фильтр (И/ИЛИ, вложенность,
  add/remove), текст фильтра в диалоге, колонки в выпадающем списке по алфавиту, lookup-колонки
  показывают выпадающий выбор — всё как до шага;
- динамический грид `/?id=140`: фильтр работает, фильтр-онли колонки (6/11) в списке.
