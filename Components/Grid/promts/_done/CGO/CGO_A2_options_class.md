> Часть серии **CGO**. Перед началом прочитай **CGO0_README_grid_options.md**.
> Требует выполненного **A1** (его таблица — вход для этого шага). Делай ТОЛЬКО этот шаг.

# A2 — класс `ClayGridOptions`

Создаём класс настроек. **Никто его пока не использует** — грид, страницы и интерфейс не
меняются. Шаг заканчивается зелёной сборкой, в которой новый файл просто лежит.

## Прочитать

- отчёт шага **A1** (список MOVE-параметров с типами и дефолтами);
- `Components/Grid/ClayGrid.razor.cs`, блок `── Parameters ──` — источник xml-doc: **текст
  докстрингов переносим дословно**, не переписываем;
- `src/Clayzor.Lib.Entities/DynamicGrid/ClayGridDynamicOptions.cs` — **другой** класс с похожим
  именем; прочитай, чтобы написать честный disambiguation-комментарий;
- `Components/Grid/ClayGrid.razor.cs` — типы `ColumnMenuMode`, `BatchOperationGroup`,
  `ColumnType`, `ClayFilterOption`: откуда они, какие using нужны.

## Создать: `Components/Grid/ClayGridOptions.cs`

Namespace — `Clayzor.Lib.Web.Controls.Components.Grid` (как у остальных файлов папки).
Класс `public sealed class ClayGridOptions`. Свойства — `{ get; set; }` с дефолтами,
**совпадающими с текущими дефолтами параметров грида до последнего значения**
(`Title = "Список"`, `Id = "clay-grid"`, `PageSize = 50`, `ShowAddButton = true`,
`ShowPagination = true`, `AllowColumnReorder = true`, `ColumnMenuMode = ColumnMenuMode.Mobile`,
`EnableValueFilter = true`, `EditSuccessMessage = "Запись обновлена"`, …).
Сверяй с отчётом A1, а не с этим абзацем.

Заголовок класса:

```csharp
/// <summary>
/// Настройки одного экземпляра грида <see cref="ClayGrid{TEntity}"/> на странице:
/// источник данных, состав тулбара, поведение колонок, фильтрации и экспорта.
/// <para>
/// Объект создаётся страницей ОДИН РАЗ и хранится в поле, а не собирается выражением
/// в разметке: грид сравнивает ссылку на параметр, и новый объект на каждый рендер
/// приводит к лишним пересчётам.
/// </para>
/// <para>
/// Не путать с <c>ClayGridDynamicOptions</c> (Clayzor.Lib.Entities.DynamicGrid): тот —
/// настройки уровня приложения (имена таблиц справочника гридов, префиксы пользовательских
/// параметров), байндятся из appsettings и живут в DI; этот — настройки конкретного грида
/// на конкретной странице.
/// </para>
/// </summary>
public sealed class ClayGridOptions
```

Порядок свойств — тематическими блоками с комментариями-разделителями в стиле остальных
файлов проекта (`// ── Источник данных ──────────`). Предлагаемые блоки:

| Блок | Свойства |
|---|---|
| Источник данных | `SelectSql`, `SearchColumns`, `DefaultOrder`, `PageSize` |
| Внешний вид | `Title`, `Id`, `ShowAddButton`, `ShowPagination` |
| Колонки | `ColumnMenuMode`, `AllowColumnReorder` |
| Фильтрация | `EnableValueFilter`, `FilterColumnTypes`, `FilterLookupOptions` |
| Редактирование | `EditDialogType`, `EditSuccessMessage` |
| Выбор и групповые операции | `SelectVisible`, `ShowPrint`, `ShowExcel`, `CustomBatchGroups` |
| Динамический режим | `Dynamic`, `DynamicGridId` |

Блоки — только визуальная группировка внутри одного плоского класса. **Никаких вложенных
объектов** (`Options.Data.SelectSql`) — см. CGO0, решение 3.

Дополнительно два члена (нужны шагу A3, больше ничего):

```csharp
/// <summary>
/// Значения по умолчанию. Используется гридом, чтобы определить, задавал ли вызывающий
/// устаревший параметр тега (сравнением с дефолтом) при одновременно заданном Options.
/// Экземпляр общий и не должен изменяться — только чтение.
/// </summary>
public static ClayGridOptions Defaults { get; } = new();

/// <summary>Создаёт копию настроек. Нужна, чтобы грид не правил объект, принадлежащий странице.</summary>
public ClayGridOptions Clone() => (ClayGridOptions)MemberwiseClone();
```

`Clone()` — поверхностная копия намеренно: коллекции (`SearchColumns`, `FilterColumnTypes`,
`CustomBatchGroups`) грид не изменяет, а глубокое копирование словарей на каждый рендер —
лишняя работа. Запиши это причиной в докстринг.

## Требования к оформлению

- **Каждое** свойство — с `/// <summary>`. Текст берётся из соответствующего параметра
  `ClayGrid` дословно; если в исходнике докстринга нет — написать, но пометить в отчёте.
- Ссылки `<see cref="..."/>` из перенесённых докстрингов должны компилироваться (в новом файле
  другой контекст). Проверь, не осталось ли `<see cref="ClayGridPageBase{T}.FilterColumnTypes"/>`
  без нужного using.
- Историческую разметку в докстрингах (`Используется начиная с задачи V7`) — сохранить как есть.
- `SearchColumns` — `string[]` с дефолтом `[]`, а не `IReadOnlyList<string>`: тип менять нельзя,
  на нём завязаны `BuildWhereClause` и вызовы в динрежиме (переход на другой тип — не этот
  рефакторинг).

## Не делай

- **Не подключай класс ни к чему.** `ClayGrid` в этом шаге не меняется — ни параметра `Options`,
  ни поля `_opt`. Если тянет «сразу и подключить» — это шаг A3, у него свой чек-лист.
- **Не переименовывай свойства** относительно параметров (`Id`, `Dynamic`, `DynamicGridId`
  остаются как есть), даже если имя неудачное. Переименования — D1.
- Не заводи вложенные секции, конструкторы с параметрами, `required`-свойства, `init`-only,
  fluent-билдер, `record`. Обычный POCO с `get; set;`: страница должна собирать его
  object-initializer'ом и при необходимости менять поле после создания.
- Не добавляй `Validate()` с семантическими проверками (пустой `SelectSql`, `Dynamic` вместе
  с `DataLoader`). Раньше грид это молча допускал — новое исключение будет изменением поведения.
- Не помещай в класс `Items`, `Loading`, `TotalCount`, `PageNumber`, `Columns`, `ColumnDefs`,
  `DataLoader` и `On*`-колбэки (CGO0, раздел «Остаются `[Parameter]`»).
- Не трогай `ClayGridDynamicOptions`.

## Проверка

- `dotnet build Clayzor.sln` — зелёный; `dotnet test` — зелёный (тесты не менялись);
- число свойств в `ClayGridOptions` = числу MOVE-параметров из отчёта A1;
- построчная сверка дефолтов: для каждого свойства значение в `ClayGridOptions` совпадает
  со значением параметра в `ClayGrid.razor.cs`. Расхождение здесь = регрессия в A3, которую
  никто не заметит до тестирования;
- `grep -rn "ClayGridOptions" src/` → попадания только в самом новом файле;
- `git diff --stat` → изменён/добавлен ровно один файл.
