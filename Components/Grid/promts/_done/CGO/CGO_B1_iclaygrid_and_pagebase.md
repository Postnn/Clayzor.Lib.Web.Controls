> Часть серии **CGO**. Перед началом прочитай **CGO0_README_grid_options.md**.
> Требует выполненного **A4**. Делай ТОЛЬКО этот шаг.

# B1 — `IClayGrid` отдаёт `Options`, `ClayGridPageBase` читает его

Сейчас интерфейс отдаёт настройки поштучно, и базовый класс страницы в каждом методе загрузки
начинается с трёх строк одинакового «распаковывания»:

```csharp
var selectSql     = Grid?.SelectSql     ?? string.Empty;
var searchColumns = Grid?.SearchColumns ?? [];
var defaultOrder  = Grid?.DefaultOrder  ?? string.Empty;
```

Это повторяется в `LoadFlatData`, `LoadGroupedData`, экспортных и печатных методах — по отчёту
A1 около десяти мест. Каждая новая настройка, нужная странице, добавляет и член интерфейса,
и строку в каждый из этих методов.

## Прочитать

- `Components/Grid/IClayGrid.cs` — целиком;
- `Components/Grid/ClayGrid.razor.cs` — блок `── IClayGrid — реализация интерфейса ──`;
- `Components/Grid/ClayGridPageBase.cs` и **все** `ClayGridPageBase.*.cs` — каждое обращение
  `Grid?.` / `Grid.`;
- `Components/Grid/ClayColumn.razor.cs` и `ClayColumnDef.razor.cs` — они тоже потребители
  `IClayGrid`; проверь, что именно читают (по отчёту A1);
- `Components/Grid/ClayColumnFilterDialog.razor`, `ClayColumnValueFilterDialog.razor` — если в
  карте A1 они значатся потребителями интерфейса.

## 1. `IClayGrid`

Добавить:

```csharp
/// <summary>
/// Действующие настройки грида. Единая точка чтения конфигурации для страницы
/// и дочерних компонентов вместо поштучных членов интерфейса.
/// </summary>
ClayGridOptions Options { get; }
```

Удалить четыре члена: `SelectSql`, `SearchColumns`, `DefaultOrder`, `EditDialogType`.
`ColumnMenuMode` — **тоже удалить**, если по карте A1 он читается только `ClayColumn`
(тогда `ClayColumn` перейдёт на `Grid.Options.ColumnMenuMode`).

Остальные члены интерфейса (`IsGrouped`, `ToggleSort`, `GetSortBadge`, `GetColumnMeta*`,
`RegisterColumn*`, `AddGroupAsync`, `AddFilterAsync`, `ActiveCompositeFilter`, `RestoreFilter`,
`OpenCompositeFilterDialog`, `IsValueFilter*`, `OpenValueFilterDialog`, `IsColumnHidden`,
`GetVisibleColumns`, `RegisterCellTemplate`, `IsGroupingTrayExpanded`, `IsFilterTrayExpanded`,
события `ColumnsChanged`/`TrayStateChanged`) — **не трогать**: это поведение, а не настройки.

## 2. Реализация в гриде

В `ClayGrid.razor.cs`, в блоке реализации интерфейса, четыре (или пять) явных реализаций
заменяются одной:

```csharp
ClayGridOptions IClayGrid.Options => _opt;
```

Отдаётся `_opt` — именно действующие настройки, а не параметр `Options` (который может быть
`null`, когда страница ещё на legacy-параметрах). До C1 это важно: страницы, не перешедшие на
`Options`, обязаны продолжать работать.

## 3. `ClayGridPageBase`

В каждом методе, где было «распаковывание», — одна строка вместо трёх:

```csharp
var opt = Grid?.Options ?? ClayGridOptions.Defaults;
```

дальше `opt.SelectSql`, `opt.SearchColumns`, `opt.DefaultOrder`. Обрати внимание на
эквивалентность дефолтов: раньше при `Grid == null` подставлялись `string.Empty` и `[]` —
у `ClayGridOptions.Defaults` ровно те же значения (проверено тестом A4), поэтому поведение
не меняется. **Если найдётся место, где старый фолбэк отличался от дефолта опций — стоп,
это не механическая замена, спроси.**

`OpenAddDialog` читает `Grid?.EditDialogType` → `opt.EditDialogType`.

Не заводи в `ClayGridPageBase` свойство-обёртку `protected ClayGridOptions GridOptions =>
Grid?.Options ?? ClayGridOptions.Defaults;` — соблазн большой, но это меняет ещё и то, как
страницы-наследники видят базовый класс, а публичная поверхность `ClayGridPageBase` в этой
серии не расширяется. Локальная переменная в каждом методе, как сейчас.

## 4. Прочие потребители интерфейса

`ClayColumn` / `ClayColumnDef` / диалоги — по карте A1 перевести на `Grid.Options.X`.
Больше в них ничего не менять.

## Не делай

- Не удаляй legacy-`[Parameter]` у грида (C1) и не помечай их `[Obsolete]`.
- Не меняй страницы (`MedicalTests.razor`, `Home.razor`) — они по-прежнему на legacy-параметрах,
  и после этого шага обязаны работать без единой правки. Это и есть доказательство, что B1
  сделан правильно.
- Не расширяй `IClayGrid` ничем, кроме `Options`; не превращай его в «интерфейс всего грида».
- Не меняй `IClayGridDataLoader` — он про загрузку данных, настроек не касается.
- Не рефактори тела методов загрузки заодно (дублирование сборки `where`/`dp` между
  `LoadFlatData` и экспортом — известная тема, отдельная задача; в отчёт, не в код).

## Проверка

- `dotnet build Clayzor.sln` + `dotnet test` — зелёные;
- `grep -rn "Grid?\.SelectSql\|Grid?\.SearchColumns\|Grid?\.DefaultOrder\|Grid?\.EditDialogType" src/`
  → ни одного попадания;
- `grep -rn "IClayGrid" src/` → у всех реализаций/потребителей компилируется, левых реализаций
  интерфейса в решении нет (проверь, что `ClayGrid<TEntity>` — единственная);
- в `IClayGrid.cs` число членов уменьшилось на 3–4 и увеличилось на 1;
- **страницы не изменены**: `git diff --stat` не содержит `MedicalTests.razor` и `Home.razor`;
- полный ручной чек-лист из CGO0 (оба стенда) — поведение идентично: страницы всё ещё передают
  legacy-параметры, а работают через `Options` внутри.
