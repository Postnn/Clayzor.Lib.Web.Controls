# ClayGrid\<T>

Универсальный компонент-грид с серверной постраничной выборкой, поиском, сортировкой, группировкой и фильтрацией по колонкам.
Используется совместно с базовым классом страницы [`ClayGridPageBase<T>`](#claygridpagebaset).

При `EditDialogType != null` грид автоматически добавляет **сервисную колонку** (первой, ширина 44px) с иконкой карандаша (`Icons.Material.Filled.Edit`). Иконка отображается только для строк детализации (`DetailRow<T>`), для заголовков групп (`GroupHeaderRow`) — пустая ячейка. При клике открывается диалог редактирования с параметром `Model = detail.Item`. После успешного сохранения показывает уведомление (`EditSuccessMessage`) и перезагружает данные.

## Параметры тега

Параметрами остаются только данные, фрагменты разметки и колбэки — всё, что **не может** жить в POCO-объекте. Конфигурация вынесена в [`ClayGridOptions`](#настройки-claygridoptions).

| Параметр | Тип | По умолчанию | Почему остался параметром |
|---|---|---|---|
| `Options` | `ClayGridOptions?` | `null` | Конфигурация — единый объект. Если не задан — используются значения по умолчанию (`ClayGridOptions.Defaults`) |
| `Items` | `IEnumerable<TEntity>` | `[]` | Меняется на каждой загрузке данных — новый объект на каждый рендер |
| `Loading` | `bool` | `false` | Меняется дважды на каждую загрузку |
| `TotalCount` | `int` | `0` | Приходит со страницы после каждого запроса |
| `PageNumber` | `int` | `1` | Синхронизация пагинатора при авто-переходах в `ToggleGroup` |
| `Columns` | `RenderFragment?` | — | Дочерняя разметка — `<ClayColumn>` компоненты |
| `ColumnDefs` | `RenderFragment?` | — | Дочерняя разметка — `<ClayColumnDef>` компоненты |
| `DataLoader` | `IClayGridDataLoader?` | `null` | Живая ссылка на страницу (`DataLoader="this"`) |
| `OnAdd` | `EventCallback` | — | EventCallback: Blazor обеспечивает ре-рендер вызывающего компонента |
| `OnGroupToggle` | `EventCallback<GroupHeaderRow>` | — | EventCallback |
| `OnQueryChanged` | `EventCallback<ClayDataQuery>` | — | EventCallback |

### Настройки (`ClayGridOptions`)

Объект создаётся страницей **один раз** и хранится в поле, а не собирается выражением в разметке: грид сравнивает ссылку на параметр, и новый объект на каждый рендер приводит к лишним пересчётам.

| Свойство | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Title` | `string` | `"Список"` | Заголовок грида |
| `Id` | `string` | `"clay-grid"` | DOM-идентификатор корневого элемента грида |
| `SelectSql` | `string` | `""` | Базовый SQL-запрос SELECT (без WHERE / ORDER BY) |
| `SearchColumns` | `string[]` | `[]` | Выходные имена колонок SELECT для полнотекстового поиска |
| `DefaultOrder` | `string` | `""` | Порядок сортировки по умолчанию |
| `PageSize` | `int` | `50` | Количество строк на странице по умолчанию |
| `ShowAddButton` | `bool` | `true` | Показывать кнопку «Добавить» в тулбаре |
| `ShowPagination` | `bool` | `true` | Показывать панель пагинации |
| `ColumnMenuMode` | `ColumnMenuMode` | `Mobile` | Режим кнопки меню (⋮) в заголовках |
| `AllowColumnReorder` | `bool` | `true` | Разрешить перетаскивание колонок |
| `EnableValueFilter` | `bool` | `true` | Глобальное включение фильтра по значению (Excel-style) |
| `FilterColumnTypes` | `IReadOnlyDictionary<string, ColumnType>` | `[]` | Тип данных для каждой фильтруемой колонки |
| `FilterLookupOptions` | `IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>>?` | `null` | Источник вариантов для выпадающего списка в диалоге фильтра |
| `EditDialogType` | `Type?` | `null` | Тип компонента диалога редактирования |
| `EditSuccessMessage` | `string` | `"Запись обновлена"` | Текст уведомления после успешного сохранения |
| `SelectVisible` | `bool` | `false` | Показывать кнопку выбора записей (чекбоксы) |
| `ShowPrint` | `bool` | `false` | Показывать группу «Печать» в меню групповых операций |
| `ShowExcel` | `bool` | `false` | Показывать группу «Выгрузка в Excel» |
| `CustomBatchGroups` | `IReadOnlyList<BatchOperationGroup>?` | `null` | Кастомные группы операций |
| `Dynamic` | `bool` | `false` | Включает динамический режим (чтение определения из БД) |
| `DynamicGridId` | `int?` | `null` | Код запроса для динамического режима |

Удалённые параметры (больше не используются):
- ~~`OnRowClick`~~ — редактирование открывается через сервисную колонку (иконка карандаша), которая добавляется автоматически при `EditDialogType != null`
- ~~`OnPrintCurrentPage`, `OnPrintAll`, `OnPrintSelected`~~ — заменены на `ShowPrint`
- ~~`OnExcelCurrentPage`, `OnExcelAll`, `OnExcelSelected`~~ — заменены на `ShowExcel`
- ~~`Groupable`~~ — группировка выполняется сервером, не MudBlazor
- ~~`GroupExpanded`~~ — состояние развёрнутости per-group, не глобальное
- ~~`GroupColumn`~~ — заменён на `GroupColumns` (множественная группировка)
- ~~`ShowGroupToggle`~~ / ~~`GroupToggleLabel`~~ — старый toggle-режим удалён
- ~~`ShowGroupingTray`~~ / ~~`AvailableGroupColumns`~~ — заменены на `ClayColumnDef` в `<ColumnDefs>`
- ~~`ShowFilterTray`~~ / ~~`AvailableFilterColumns`~~ — заменены на `ClayColumnDef` в `<ColumnDefs>`
- ~~`ChildContent`~~ — переименован в `Columns`, заодно добавлен `ColumnDefs`
- ~~`OnQueryChanged`~~ — заменён на `DataLoader` (IClayGridDataLoader)
- Конфигурационные параметры сведены в `ClayGridOptions` (серия CGO): `Title`, `Id`, `ShowAddButton`, `PageSize`, `EditSuccessMessage`, `ShowPagination`, `AllowColumnReorder`, `FilterColumnTypes`, `FilterLookupOptions`, `SelectSql`, `SearchColumns`, `DefaultOrder`, `EditDialogType`, `ColumnMenuMode`, `SelectVisible`, `ShowPrint`, `ShowExcel`, `EnableValueFilter`, `CustomBatchGroups`, `Dynamic`, `DynamicGridId`

## Публичные методы

| Метод | Описание |
|---|---|
| `async Task ToggleSort(string sqlCol)` | Циклически переключает сортировку: ASC → DESC → убрать. Возвращает `Task` — **обязательно awaitable** |
| `GetSortBadge(string sqlCol)` | Возвращает `RenderFragment` с бейджем сортировки (номер + стрелка) |
| `RefreshAsync()` | Сбрасывает номер страницы на 1 и вызывает `OnQueryChanged` |
| `GroupColumns` | `IReadOnlyList<string>` — текущий список SQL-имён колонок в трее группировки |
| `IsGrouped(string sqlName)` | Возвращает `true`, если колонка участвует в группировке |
| `GetColumnMeta(string sqlName)` | Метаданные колонки по SQL-имени |
| `GetColumnMetaById(int columnId)` | Метаданные колонки по числовому `ColumnId` |
| `GetGroupByOrder(string sqlColumn)` | Порядок группировки для SQL-колонки (позиция в трее) |
| `AddGroupAsync(string sqlName)` | Добавляет колонку в трей группировки (альтернатива drag-and-drop) |
| `AddFilterAsync(string sqlName)` | Открывает диалог фильтрации для колонки (альтернатива drag-and-drop) |

## ClayColumnDef

Невидимый компонент-регистратор метаданных колонки. Размещается внутри `<ColumnDefs>`. При инициализации регистрирует метаданные в `IClayGrid` через каскадный параметр.

| Параметр | Тип | По умолчанию | Обязательный | Описание |
|---|---|---|---|---|
| `ColumnId` | `int` | — | ✓ `EditorRequired` | Числовой идентификатор — связь с `ClayColumn` |
| `SqlName` | `string` | `""` | — | SQL-имя колонки — выходное имя из SELECT |
| `DisplayName` | `string` | `""` | — | Отображаемое имя (заголовок колонки, чипы треев) |
| `SortName` | `string?` | `null` | — | Имя для ORDER BY. Если `null` — используется `SqlName` |
| `Groupable` | `bool` | `false` | — | Разрешить группировку по колонке |
| `Filterable` | `bool` | `false` | — | Разрешить фильтрацию по колонке |
| `AllowValueFilter` | `bool` | `false` | — | Включить фильтр по уникальному значению (Excel-style). Работает только при `Filterable=true` |
| `BoolTrueLabel` | `string?` | `null` | — | Подпись `true` для булевой колонки (напр. «Только IT оборудование»). `null` → «Да» |
| `BoolFalseLabel` | `string?` | `null` | — | Подпись `false` для булевой колонки (напр. «Не IT оборудование»). `null` → «Нет» |

## ClayColumn\<TEntity>

Колонка грида с автоматически построенным заголовком. Получает метаданные (`DisplayName`, `SqlName`, `SortName`, `Groupable`) из зарегистрированного `ClayColumnDef` по числовому `ColumnId`.

**Авто-возможности:**
- Title из `ClayColumnDef.DisplayName`
- HeaderTemplate с серверной сортировкой (`Grid.ToggleSort`)
- Заголовок содержит `data-col-sql` для кастомного insert-drag (через `clayGridColumnDrag.js`)
- `ClayDragState.DraggedColumn` устанавливается через JS→C# `SetDraggedColumn` при dragstart — tray-drop продолжает работать
- Автоматическое скрытие колонки при группировке (`Hidden = IsGrouped(SqlName)`)
- Кнопка меню `⋮` (мобильные / `ColumnMenuMode`) — альтернативный вход для группировки и фильтрации без drag-and-drop
- **`DragAndDropEnabled="false"`** — MudBlazor `DragDropColumnReordering` **не используется**. Перемещение колонок реализовано кастомным JS с insert-семантикой (вставка перед/после)

| Параметр | Тип | Обязательный | Описание |
|---|---|---|---|
| `ColumnId` | `int` | ✓ `EditorRequired` | Числовой идентификатор — связь с `ClayColumnDef` |
| `CellTemplate` | `RenderFragment<CellContext<TEntity>>?` | — | Шаблон содержимого ячейки |

## ClayGroupHeader

Стандартный заголовок строки группировки. Отображает иконку раскрытия/сворачивания, название группы и количество элементов.
`ClayGrid` рендерит его сам в вычисленной хост-колонке (см. «Рендеринг групп» ниже) — страница-потребитель
компонент напрямую не вызывает.

| Параметр | Тип | Описание |
|---|---|---|
| `Header` | `GroupHeaderRow` | Данные строки заголовка группы |
| `OnToggle` | `EventCallback<GroupHeaderRow>` | Вызывается при клике на иконку раскрытия/сворачивания |

## ClayDragState

Статическое хранилище имени перетаскиваемой SQL-колонки между dragstart и drop. Используется вместо `DataTransfer.GetData`, недоступного в Blazor .NET.

```csharp
public static class ClayDragState
{
    public static string? DraggedColumn { get; set; }
}
```

## ClayDataQuery

Класс состояния запроса, передаваемый в `IClayGridDataLoader.OnQueryChangedAsync()`:

- `SearchText` — текст поиска
- `GroupEnabled` — включена ли группировка
- `GroupColumns` — список SQL-имён колонок группировки в порядке приоритета
- `ExpandedGroups` — `HashSet<string>` полных ключей развёрнутых групп (разделитель `\u001F`)
- `SortColumns` — список `SortColumn(Column, Desc)`
- `PageNumber` — номер текущей страницы (1-based)
- `PageSize` — размер страницы
- `TotalCount` — общее число записей (заполняется страницей после загрузки)
- `ColumnFilters` — `[Obsolete]` `Dictionary<string, ColumnFilter>` — заменён на `CompositeFilter`
- `CompositeFilter` — `ClayFilterGroupNode?` — дерево составного фильтра, единый источник истины фильтрации. `null` или пустой корень — без фильтрации
- `BuildColumnFilterClause(DynamicParameters, columnNameMap?)` — `[Obsolete]`. Заменён на `BuildCompositeFilterClause` / `ClayCompositeSqlBuilder.Build`
- `BuildOrderBy(defaultOrder)` — строит `ORDER BY`; при включённой группировке все `GroupColumns` идут первыми
- `BuildWhereClause(searchColumns)` — строит `WHERE ... LIKE @search`
- `CombineWhere(string?, string?)` — объединяет два WHERE-фрагмента через `AND`

## IClayGrid

Интерфейс, реализуемый `ClayGrid<TEntity>`. Используется тремя потребителями:
- `ClayColumnDef` — регистрация метаданных через каскадный параметр
- `ClayColumn<TEntity>` — поиск метаданных по `ColumnId` для авто-заголовка
- `ClayGridPageBase<T>` — чтение SQL-настроек грида

| Член | Тип | Описание |
|---|---|---|
| `Options` | `ClayGridOptions` | Действующие настройки грида — единая точка чтения конфигурации |
| `IsGrouped(sqlName)` | `bool` | Участвует ли в группировке |
| `ToggleSort(sqlName)` | `Task` | Переключение сортировки |
| `GetSortBadge(sqlName)` | `RenderFragment` | Бейдж сортировки |
| `GetColumnMeta(sqlName)` | `ClayColumnMeta?` | Метаданные по SQL-имени |
| `GetColumnMetaById(columnId)` | `ClayColumnMeta?` | Метаданные по ID |
| `RegisterColumn(columnId, sqlName, displayName, groupable, filterable, sortName?, allowValueFilter?, boolTrueLabel?, boolFalseLabel?)` | `void` | Регистрация колонки |
| `UnregisterColumn(columnId, sqlName)` | `void` | Отмена регистрации |
| `ColumnsChanged` | `event Action?` | Событие изменения реестра |
| `TrayStateChanged` | `event Action?` | Событие открытия/закрытия панелей |
| `IsGroupingTrayExpanded` | `bool` | Открыта ли панель группировки |
| `IsFilterTrayExpanded` | `bool` | Открыта ли панель фильтрации |
| `AddGroupAsync(sqlName)` | `Task` | Добавить колонку в трей группировки |
| `AddFilterAsync(sqlName)` | `Task` | Открыть диалог фильтра для колонки |
| `ActiveCompositeFilter` | `ClayFilterGroupNode?` | Текущее дерево фильтра (null/пустой — без фильтрации) |
| `OpenCompositeFilterDialog()` | `Task` | Открыть диалог составного фильтра (UI — задача 11) |

## ClayColumnMeta

Метаданные зарегистрированной колонки (readonly, init-only свойства):

| Свойство | Тип | Описание |
|---|---|---|
| `ColumnId` | `int` | Числовой идентификатор |
| `SqlName` | `string` | SQL-имя (выходное имя SELECT) |
| `DisplayName` | `string` | Отображаемое имя |
| `SortName` | `string` | Имя для ORDER BY (по умолчанию = SqlName) |
| `Groupable` | `bool` | Разрешена группировка |
| `Filterable` | `bool` | Разрешена фильтрация |
| `AllowValueFilter` | `bool` | Разрешён фильтр по уникальному значению (Excel-style) |
| `BoolTrueLabel` | `string?` | Подпись `true` для булевой колонки |
| `BoolFalseLabel` | `string?` | Подпись `false` для булевой колонки |
| `Type` | `ColumnTypeDescriptor` | Дескриптор типа колонки |

## DistinctValuesResult

Результат загрузки уникальных значений колонки через `IClayGridDataLoader.LoadDistinctValuesAsync()`. Используется диалогом фильтра по значению (V6).

| Свойство | Тип | Описание |
|---|---|---|
| `Values` | `IReadOnlyList<object?>` | Уникальные значения (без пустышек), не больше лимита. Пусто, если `Capped=true`. Типы сохранены (int, string, DateTime, bool, …) |
| `Capped` | `bool` | `true` — уникальных значений больше лимита (100), список `Values` пуст |
| `HasBlanks` | `bool` | `true` — в колонке есть `NULL` или пустые строки |
| `TotalDistinct` | `int` | Полное количество уникальных не-пустых значений (для инверсии), когда `Capped=false` |

## Серверная группировка

Группировка выполняется **на стороне SQL Server** (не MudBlazor). Реализация — `ClayGroupingEngine` (статический класс). Два отдельных запроса:

1. **Групповые агрегаты**: `GROUP BY` + `COUNT(*)`, возвращает уникальные значения и количество записей
2. **Детальные строки**: выборка с `ROW_NUMBER()` и фильтром по значениям группы

### Модель данных

- `IClayGridRow` — маркерный интерфейс строки в плоском списке
- `GroupHeaderRow` — заголовок группы с `FullKey`, `DisplayValue`, `ItemCount`, `Depth`, `IsExpanded`
- `DetailRow<T>` — обёртка сущности с `Item`, `GroupKey`, `Depth`
- `GroupedPage<T>` — результат: `Rows` (плоский список `IClayGridRow`) + `TotalEffectiveRows`

### Рендеринг групп

Грид сам рендерит `<ClayGroupHeader>` для строк `GroupHeaderRow` — выбирает единственную
«хост-колонку» (`GroupRowHostKey`/`IsGroupRowHost` в `ClayGrid.Grouping.cs`, приоритет: колонка
редактирования → первая видимая колонка данных, не скрытая группировкой) и вызывает его сам.
Страница-потребитель ничего не пишет в `CellTemplate` — только подписывается на событие:

```razor
<ClayGrid TEntity="IClayGridRow" ...
           OnGroupToggle="ToggleGroup">
    ...
    <ClayColumn TEntity="IClayGridRow" ColumnId="2">
        <CellTemplate>
            @if (context.Item is DetailRow<MedicalTest> detail)
            {
                <MudText Style="@($"padding-left:{(detail.Depth + 1) * 16}px")">@detail.Item.Id</MudText>
            }
        </CellTemplate>
    </ClayColumn>
</ClayGrid>
```

Проверять `is GroupHeaderRow` внутри `CellTemplate` колонки данных больше не нужно — грид туда не
заходит для строк-заголовков групп. Хост-колонка не зависит от того, какая колонка сейчас скрыта
группировкой, поэтому группировка по «первой» колонке больше не приводит к пропаданию заголовка.

**Запрещено** использовать `PropertyColumn` с `Groupable`/`Grouping`/`GroupBy`/`GroupTemplate` — эти атрибуты удалены.

### `ExpandedGroups` и пагинация

- Состояние развёрнутости хранится в `ClayDataQuery.ExpandedGroups` (`HashSet<string>` ключей)
- Каждый заголовок группы = 1 эффективная строка, каждая строка детализации = 1
- `TotalCount` = общее эффективное количество строк
- При сворачивании/разворачивании группы количество видимых строк меняется
- При разворачивании последней группы на странице — авто-переход на следующую страницу
- При сворачивании группы, если `PageNumber > ceil(TotalCount / PageSize)` — авто-возврат на `maxPage`
- Позиция прокрутки сохраняется при раскрытии/сворачивании (`clayGridScroll.js`: capture → toggle → restore), кроме случаев авто-перехода страницы (GB13)

### Сохранение порядка агрегатов из БД

При построении дерева групп **запрещено** пересортировывать список агрегатов после получения из БД:

```csharp
// НЕПРАВИЛЬНО — уничтожает порядок ORDER BY из SQL:
foreach (var a in aggregates.OrderBy(a => a.FullKey)) { ... }

// ПРАВИЛЬНО — порядок из БД сохраняется:
foreach (var a in aggregates) { ... }
```

Синтетические родительские узлы строятся внутри того же цикла `foreach (var gr in groupRows)` **перед** листовым узлом, поэтому при прямом обходе `aggregates` инвариант «родитель встречается раньше дочернего» всегда соблюдается.

### Имена колонок в WHERE и GROUP BY

- `SearchColumns` передаются как выходные имена (например, `"НазваниеАнализа"`, `"TestTypeName"`)
- SQL-пагинация через `ROW_NUMBER()` оборачивает SELECT в подзапрос `FROM (SELECT ...) _src`, где выходные имена колонок видны напрямую — алиасы таблиц не нужны
- `GroupColumns` содержат те же выходные имена — они напрямую используются в `GROUP BY`

## Серверная фильтрация по колонкам

Фильтрация по колонкам выполняется **на стороне SQL Server** через `BuildColumnFilterClause`.
UI — панель фильтров (filter tray) с drag-and-drop заголовков и диалогом `ClayColumnFilterDialog` для настройки условий.

### Модель данных
- `ColumnType` — тип данных колонки:
  - `Text` — строки (10 операторов: Contains/NotContains/Equals/NotEquals/StartsWith/NotStartsWith/EndsWith/NotEndsWith/IsEmpty/IsNotEmpty)
  - `Number` — целые числа (int/long/short/byte: равенство + сравнения >/</>=/<=, IsNull, IsNotNull)
  - `Decimal` — дробные числа (decimal/double/float: те же что Number, редактор `MudNumericField<decimal?>`)
  - `Date` — даты (DateTime/DateTimeOffset/DateOnly: сравнения, IsNull, IsNotNull, редактор `MudDatePicker`)
  - `Boolean` — булевы (Equals, IsNull, IsNotNull)
- `ColumnFilterOperator` — оператор сравнения: все 14 значений + `IsNull`, `IsNotNull`
- `LogicalOperator` — `And` / `Or` для объединения двух условий на одной колонке
- `ColumnFilter` — условие фильтра: `Column` (SQL-имя), `ParamName` (имя Dapper-параметра), `Operator`, `Value` + опциональные `LogicalOperator`, `SecondOperator`, `SecondValue`, `SecondParamName` (до двух условий на колонку). `HasValue` учитывает `IsNull`/`IsNotNull`
- `ClayDataQuery.ColumnFilters` — `[Obsolete]` — заменён на `CompositeFilter`
- `ClayDataQuery.CompositeFilter` — `ClayFilterGroupNode?` — единый источник истины фильтрации. `null` или пустой корень — без фильтрации
- `ClayFilterOption` — вариант значения для выпадающего списка в диалоге фильтра (`Value`, `Label`)
- `ClayGridPageBase.FilterLookupOptions` — virtual-словарь (SqlName → список `ClayFilterOption`) для замены текстового/числового редактора на `MudSelect`

### Дескрипторы типов (`Components/Grid/ColumnTypes/`)
- `ColumnTypeDescriptor` — абстрактный дескриптор: `Kind`, `ClrType`, `Operators`, `DefaultOperator`, `OperatorTakesValue(op)`, `Parse(string?)`, `Format(object?)`
- Конкретные: `TextColumnType`, `NumberColumnType` (int), `DecimalColumnType` (decimal), `BooleanColumnType`, `DateColumnType` (DateTime)
- `ColumnTypeRegistry` — `FromClr(Type)` / `FromKind(ColumnType)`, синглтоны
- `ClayColumnMeta.Type` — дескриптор заполняется при регистрации колонки
- Новый тип колонки = один класс-дескриптор + строка в реестре

### Составной фильтр (дерево И/ИЛИ)

Типы в `Components/Grid/Filter/`:
- `IClayFilterNode` — интерфейс узла дерева с рекурсивным `Clone()`
- `ClayFilterGroupNode` — группа: `Logic` (And/Or), `Nodes` (List\<IClayFilterNode\>), `Clone()`. Переиспользует существующий `LogicalOperator`
- `ColumnFilter` реализует `IClayFilterNode` — листовой узел. `Source` (`ClayFilterSource`) — `ColumnDialog` (диалог колонки) или `CompositeDialog` (настраиваемый фильтр). `IsNew` (`[JsonIgnore]`, не копируется) — транзиентный флаг для автофокуса на «Значение»
- `ClayCompositeSqlBuilder` — статический SQL-билдер для дерева фильтра. `Build(root, parameters, knownColumns, columnNameMap?)` рекурсивно обходит дерево `ClayFilterGroupNode` → фрагмент WHERE (без слова WHERE). Безопасность: колонка только из белого списка `knownColumns`, значения — Dapper-параметры, имена параметров — сквозной счётчик. Переиспользует `ClayDataQuery.BuildSingleClause`

### Хранение в гриде и интеграция с данными (задача 10)
- `ClayGrid._filterRoot` (`ClayFilterGroupNode`, `Logic=And`) — приватный корень дерева, единый источник истины. Заменяет `_activeFilters` (словарь, неявное AND)
- Колоночные фильтры (`ClayColumnFilterDialog`) — листья с `Source=ColumnDialog`, добавляются/заменяются в `_filterRoot.Nodes`; отображаются чипами в трее через `ColumnDialogLeaves`
- Составной фильтр (`ClayFilterDialog`) — поддерево с `Source=CompositeDialog` (весь `_filterRoot` синхронизируется в `query.CompositeFilter`). Колонки в выпадающем списке отсортированы по алфавиту (GB15). Новое условие создаётся без предвыбранной колонки — пользователь выбирает сам (GB16). Один скролл у дерева условий, `overflow:hidden` на `.mud-dialog-content` (GB17).
- `ToggleFilterTray()` **не сбрасывает** фильтр при сворачивании панели. Сброс — только явной кнопкой `ClearAllFilters()` (обнуляет `_filterRoot` целиком)
- `ClayGridPageBase.BuildCompositeFilterClause(CompositeFilter?, dp, columnNameMap?)` — обёртка над `ClayCompositeSqlBuilder.Build`, вызывает `BuildKnownColumns()` (белый список из `_inferredColumnTypes.Keys`). Заменяет старый `_query.BuildColumnFilterClause(dp)` во **всех** путях загрузки (7 мест): `LoadFlatData`, `LoadGroupedData`, `GetGroupLeafRows`, экспорт в Excel (плоский + сгруппированный), печать (плоская + сгруппированная), экспорт/печать выбранных (плоский + сгруппированный)

### SQL-генерация
- `ClayDataQuery.BuildColumnFilterClause(...)` — `[Obsolete]`. Заменён на `BuildCompositeFilterClause` / `ClayCompositeSqlBuilder.Build`
- `ClayDataQuery.BuildSingleClause(colName, paramName, op, value, dp)` — строит SQL-выражение для одного условия фильтрации (`internal`). Вызывается из `ClayCompositeSqlBuilder` для листовых узлов
- `ClayCompositeSqlBuilder.Build(root, parameters, knownColumns, columnNameMap?)` — строит WHERE из дерева фильтра. Группы → скобки + `AND`/`OR`; листья → `BuildSingleClause` с проверкой белого списка колонок
- `columnNameMap` — опциональный маппинг имён для плоского режима, где имена колонок в SELECT отличаются от подзапросного режима
- `ColumnFilter.IsNew` — транзиентный UI-флаг (`[JsonIgnore]`, не копируется в `Clone()`): свежедобавленное перетаскиванием условие → автофокус на «Значение»

### Filter tray
- Панель включается кнопкой `FilterAlt`, скрыта по умолчанию. Кнопка появляется автоматически при наличии хотя бы одного `ClayColumnDef` с `Filterable="true"`
- Иконка `FilterList` в левой части панели — кликабельный `ClayButton` (с классом `filter-tray-icon`), открывает `OpenCompositeFilterDialog()` → `ClayFilterDialog`
- **Два взаимоисключающих режима** отображения чипов (свойство `HasComposite` — любой узел не-ColumnDialog):
  - **Есть составные условия** → единый текстовый чип со строкой `BuildFilterDescription()`. Клик → `OpenCompositeFilterDialog()`. Крестик → `ClearAllFilters()`. Чипов колонок нет
  - **Нет составных условий** → чипы по листьям `ColumnDialog` (один чип на колонку, сегменты кликабельны → `OpenFilterDialog`)
- **Перетаскивание колонки на панель** (`OnFilterTrayDrop`):
  - Нет составных условий → `OpenFilterDialog(sqlName)` (диалог колонки), лист `Source=ColumnDialog`
  - Есть составные условия → `BuildTreeWithColumnAnded(sqlName)` строит копию дерева с новым условием через `И` на верхнем уровне. Если корень `ИЛИ` — оборачивает в `И(Старое, Новое)`. Открывает диалог на `seedRoot`; отмена не меняет действующий фильтр. У нового листа `IsNew=true` → автофокус на «Значение»
- Удаление колоночного фильтра: × на чипе → `RemoveFilter(sqlName)`. Колоночные условия можно удалить и из формы `ClayFilterDialog` (крестик в `ClayFilterGroup`)
- **Печатная шапка** (`.clay-grid-print-descriptions`) скрыта на экране (`display:none`), видна только при печати (`@media print { display:block }`) — дублирования текста фильтра на экране нет
- Сегменты/описание строятся через `ClayFilterDescriptionBuilder`: `BuildSegments(root, getDisplayName, getColumnMeta?)` → `IReadOnlyList<FilterSegment>` (для колоночных чипов, V8: включает `ValueFilter`); `BuildText(root, getDisplayName, getColumnMeta?)` → строка для составного чипа и экспорта/печати (V8: включает `ValueFilter`); `DescribeValueFilter(vf, getDisplayName, getColumnMeta?)` → читаемое описание фильтра по значению («одно из [...]» / «кроме [...]»)
- Filter tray не конфликтует с grouping tray — оба могут быть открыты одновременно

### Интеграция на странице

Конфигурация SQL передаётся через параметры `<ClayGrid>`. `ClayGridPageBase` автоматически читает их из `IClayGrid`:

```razor
<ClayGrid TEntity="IClayGridRow"
           DataLoader="this"
           SelectSql="@SQLQueries.SELECT_МоиЗаписи"
           SearchColumns="@(new[]{"НазваниеАнализа","TestTypeName"})"
           DefaultOrder="Порядок, НазваниеАнализа"
           EditDialogType="@typeof(MyEditDialog)"
           FilterColumnTypes="@FilterColumnTypes"
           ... >
```

В плоском и группированном режимах используются одни и те же `SearchColumns` — выходные имена колонок видны в подзапросе `ROW_NUMBER()`. `FilterColumnTypes` вычисляется автоматически через рефлексию. Фильтрация во всех путях загрузки — через `BuildCompositeFilterClause(_query.CompositeFilter, dp)` (дерево `ClayFilterGroupNode`), заменившую `BuildColumnFilterClause` (словарь-AND).

## ClayGridPageBase\<T>

Базовый класс Blazor-страницы. Инкапсулирует весь инфраструктурный код загрузки данных:
плоский режим и режим группировки.

### Паттерн использования

Страница-наследник:
1. Наследуется: `@inherits ClayGridPageBase<MyEntity>`
2. Передаёт настройки через `Options="_gridOptions"` — объект `ClayGridOptions`, собранный в `OnInitialized`
3. Передаёт `DataLoader="this"` — подключает `IClayGridDataLoader`
4. Переопределяет свойство `Grid`: `protected override IClayGrid? Grid => _dataGrid;`
5. Объявляет поле `private ClayGrid<IClayGridRow> _dataGrid = null!;` для `@ref`

### Virtual-свойства (могут быть переопределены)

| Свойство | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Grid` | `IClayGrid?` | `null` | Ссылка на грид — **обязательно переопределить**: `protected override IClayGrid? Grid => _dataGrid;` |
| `AddSuccessMessage` | `string` | `"Запись добавлена"` | Текст уведомления после добавления |
| `SaveSuccessMessage` | `string` | `"Запись обновлена"` | Текст уведомления после сохранения |
| `FilterColumnTypes` | `IReadOnlyDictionary<string, ColumnType>` | Авто-вычисляется | Типы колонок (рефлексия по `[Column]` и C#-типам) |

### Инжектируемые сервисы

`DbManager Db`, `ISnackbar Snackbar` и `IDialogService DialogService` — инжектируются автоматически, объявлять на странице не нужно.

### Методы (не переопределяются)

| Метод | Описание |
|---|---|
| `LoadData()` | Диспетчер: вызывает LoadGroupedData или LoadFlatData в зависимости от состояния группировки |
| `ToggleGroup(GroupHeaderRow)` | Раскрытие/сворачивание группы с авто-пагинацией |
| `OpenAddDialog()` | Открывает диалог добавления (тип из `EditDialogType`) |

### Поля (protected, доступны в разметке)

| Поле | Тип | Описание |
|---|---|---|
| `_query` | `ClayDataQuery` | Текущее состояние запроса |
| `_rows` | `List<IClayGridRow>` | Строки текущей страницы |
| `_loading` | `bool` | Признак загрузки |

### Шаблон страницы

```razor
@page "/my-entity"
@using Clayzor.Lib.Web.Settings
@using Clayzor.Lib.Web.Controls
@inherits ClayGridPageBase<MyEntity>
@inject ClayAppSettings AppSettings

<PageTitle>Мои записи</PageTitle>

<ClayGrid TEntity="IClayGridRow"
           @ref="_dataGrid"
           DataLoader="this"
           Title="Мои записи"
           SelectSql="@SQLQueries.SELECT_МоиЗаписи"
           SearchColumns="@(new[]{"НазваниеАнализа","TestTypeName"})"
           DefaultOrder="Порядок, НазваниеАнализа"
           EditDialogType="@typeof(MyEditDialog)"
           Items="_rows"
           Loading="_loading"
           PageSize="@AppSettings.DefaultPageSize"
           FilterColumnTypes="@FilterColumnTypes"
            TotalCount="@_query.TotalCount"
            PageNumber="@_query.PageNumber"
            ShowPagination="true"
            OnAdd="OpenAddDialog"
            OnGroupToggle="ToggleGroup">

    <ColumnDefs>
        <ClayColumnDef ColumnId="1" SqlName="TestTypeName"            DisplayName="Тип"      Groupable="true" Filterable="true" />
        <ClayColumnDef ColumnId="2" SqlName="КодЗаписи"              DisplayName="Код"      Groupable="true" Filterable="true" />
        <ClayColumnDef ColumnId="3" SqlName="НазваниеЗаписи"         DisplayName="Название" Groupable="true" Filterable="true" />
    </ColumnDefs>

    <Columns>

        <ClayColumn TEntity="IClayGridRow" ColumnId="2">
            <CellTemplate>
                @if (context.Item is DetailRow<MyEntity> detail)
                {
                    <MudText Style="@($"padding-left:{(detail.Depth + 1) * 16}px")">@detail.Item.Id</MudText>
                }
            </CellTemplate>
        </ClayColumn>

        <ClayColumn TEntity="IClayGridRow" ColumnId="3">
            <CellTemplate>
                @if (context.Item is DetailRow<MyEntity> detail)
                {
                    <MudText>@detail.Item.Name</MudText>
                }
            </CellTemplate>
        </ClayColumn>

        <ClayColumn TEntity="IClayGridRow" ColumnId="1">
            <CellTemplate>
                @if (context.Item is DetailRow<MyEntity> detail)
                {
                    <MudText>@detail.Item.TestTypeName</MudText>
                }
            </CellTemplate>
        </ClayColumn>

    </Columns>

</ClayGrid>

@code {
    private ClayGrid<IClayGridRow> _dataGrid = null!;
    protected override IClayGrid? Grid => _dataGrid;
}
```

### Codebehind-структура

После рефакторинга (задача 06 мастер-плана) логика `ClayGridPageBase<T>` разнесена по 5 partial-файлам. Все файлы объявляют `public abstract partial class ClayGridPageBase<T> where T : Entity` в namespace `Clayzor.Lib.Web.Controls.Components.Grid`. Базовый класс (`ComponentBase`) и реализуемые интерфейсы (`IClayGridDataLoader`) — только в основном файле.

| Файл | Строк | Содержание |
|---|---|---|
| `ClayGridPageBase.cs` | 365 | Ядро: `[Inject]`-сервисы (`Db`, `Snackbar`, `DialogService`, `JS`), свойство `Grid`, поля `_query`/`_rows`/`_loading`/`_totalGroupCount`, `OnAfterRenderAsync`, `LoadData`, `LoadFlatData`, `LoadGroupedData`, `ToggleGroup`, `OpenAddDialog`, `Dispose`, интерфейс `IClayGridDataLoader` |
| `ClayGridPageBase.ColumnTypes.cs` | 83 | Вывод типов колонок: `_idColumnName`, `_propertyMap`, `_inferredColumnTypes`, `FilterColumnTypes`, `GetIdColumnName`, `BuildPropertyMap`, `InferFilterColumnTypes`, `MapClrTypeToColumnType` |
| `ClayGridPageBase.Export.Excel.cs` | 208 | Экспорт в Excel: `IClayGridDataLoader.ExcelExportAsync`, `BuildAllRowsForExcel`, `BuildAllGroupedRowsForExcel`, `BuildExportRows`, `CollectCounts`, `SanitizeFileName` |
| `ClayGridPageBase.Export.Print.cs` | 89 | Печать всех данных: `BuildAllRowsForPrint`, `BuildAllFlatRowsForPrint`, `BuildAllGroupedRowsForPrint` |
| `ClayGridPageBase.Export.Selected.cs` | 225 | Экспорт/печать выбранных: `BuildPrintHtmlForSelectedAsync`, `BuildAllRowsForSelected`, `BuildAllFlatRowsForSelected`, `BuildAllGroupedRowsForSelected`, `GetGroupKeysByDepth`, `CollectKeysByDepth` |

**Правила модификации:**
- Новые поля/методы добавлять в соответствующий тематический файл, а не в `ClayGridPageBase.cs`
- При добавлении using — в тот файл, где используется тип
- Базовый класс и интерфейсы — только в `ClayGridPageBase.cs`

## Состояния

- **Поиск** — сбрасывает страницу на 1, вызывает `OnQueryChangedAsync` с debounce 300 мс
- **Сортировка** — до 2 колонок, циклически: ASC → DESC → убрать. Сбрасывает страницу на 1. Сортировка по чипу в трее также работает (направление учитывается в `GROUP BY ... ORDER BY`). **`ToggleSort` возвращает `Task` — вызывать только через `await`**, иначе Blazor не дождётся перезагрузки данных
- **Группировка (tray)** — панель над гридом, скрытая по умолчанию. Открывается кнопкой `AccountTree` в тулбаре (класс `grouping-toggle-btn`). Добавление колонок — перетаскивание заголовка на панель (drag встроен в `ClayColumn`). Удаление — клик по × на чипе. Изменение порядка — перетаскивание чипов. Сортировка по чипу — клик по его названию (бейдж `chip-sort-badge`). Переключатель `UnfoldMore`/`UnfoldLess` на чипе — разворачивает/сворачивает ВСЕ группы этого уровня (с каскадом вверх). Кнопка `MoreVert` (⋮) — контекстное меню с пунктом «Фильтровать» (аналог меню заголовка колонки). При любом изменении сбрасывается страница на 1
- **Фильтрация (tray)** — панель над гридом, скрытая по умолчанию. Открывается кнопкой `FilterAlt` в тулбаре (класс `filter-toggle-btn`). Добавление фильтра — перетаскивание заголовка (drag встроен в `ClayColumn`) на панель → открывается `ClayColumnFilterDialog`. Редактирование — клик по чипу. Удаление — клик по × на чипе. При выключении трея все фильтры сбрасываются. При любом изменении сбрасывается страница на 1
- **Сворачивание/разворачивание группы** — НЕ сбрасывает страницу на 1. Если детали не влезают — авто-переход вперёд
- **Режим выбора** — включается кнопкой `CheckBox` в тулбаре. Добавляет сервисную колонку с компонентом `ClayCheckbox` (16×16px, белый фон, CSS-галочка border-rotate, tri-state с жирным квадратом для indeterminate). Выделение хранится как `HashSet<int>` (ID сущностей) и **персистентно** между страницами. Галка в заголовке управляет и `DetailRow`, и `GroupHeaderRow` (через ленивую загрузку дочерних ID → `_groupChildIds`). Сбрасывается при изменении поиска, группировки, сортировки или фильтров
- **Смена размера страницы** — сбрасывает страницу на 1. Выделение **не сбрасывается**
- **Кнопка «Обновить»** — сбрасывает страницу на 1, перезагружает данные
- **Переход по страницам** — кнопки `|<`, `<`, `>`, `>|`. Не сбрасывают фильтры
- **Защита выхода за границы** — при уменьшении `TotalCount` номер страницы автоматически обрезается до максимального
- **Экспорт в Excel** — на время выгрузки рядом с заголовком грида показывается `MudProgressCircular` (Size.Small, Indeterminate). Флаг `_isExporting` устанавливается в `true` перед вызовом `DataLoader.ExcelExportAsync()` и сбрасывается в `false` в `finally`-блоке. После завершения появляется снекбар с именем файла или ошибкой
- **Печать всех данных** — `_isExporting` = true → `DataLoader.BuildPrintHtmlAsync(columns, title, ...)` (загружает все строки в отдельный список, не трогая `_rows`) → `ClayGridPrintHtmlGenerator.Build()` (генерирует HTML с инлайн-стилями) → `clayGridPrint.printHtml(html)` (рендерит в скрытый iframe, печатает, удаляет). Грид НЕ модифицируется. Свёрнутые группы печатаются только заголовком

## Кнопки тулбара

Все кнопки в строке заголовка — `ClayButton` с тултипом. Не использовать `MudButton Variant.Filled`.

| Кнопка | Иконка | CSS-класс | Поведение |
|---|---|---|---|
| Группировка | `AccountTree` | `grouping-toggle-btn` / `grouping-toggle-btn--active` | Показывает/скрывает панель tray |
| Фильтрация | `FilterAlt` | `filter-toggle-btn` / `filter-toggle-btn--active` | Показывает/скрывает панель фильтрации |
| Добавить | `Add` | `toolbar-add-btn` | Вызывает `OnAdd` |
| Выбрать записи | `CheckBox` | `toolbar-select-btn` / `toolbar-select-btn--active` | Включает/выключает чекбоксы в строках |
| Групповые операции | `PlaylistAddCheck` | `toolbar-batch-btn` | Меню: Печать / Excel (текущая страница, выбранные, все данные) |
| Документация | `MenuBook` | `toolbar-help-btn` | Открывает URL справки в новой вкладке. Адрес задаётся ключом `URI_help_clayGrid` в `web.config` → `appSettings`. Если ключ отсутствует или пуст — кнопка скрыта |

## Быстрый поиск в динамическом режиме

В динамическом режиме поле поиска управляется через колонку `УчаствуетВБыстромПоиске` (tinyint) в таблице `ClayGridColumns`. Администратор задаёт значения по умолчанию; пользователь может переопределить их в диалоге «Настройка колонок» (кнопка `ViewColumn`).

### Принцип работы

1. **Наличие колонки**: если `УчаствуетВБыстромПоиске` отсутствует в `ClayGridColumns` — быстрого поиска нет, поле скрыто.
2. **Админские значения**: колонки с `УчаствуетВБыстромПоиске=1` участвуют в поиске по умолчанию.
3. **Пользовательский выбор**: сохраняется в `ClayGridUserParams` (ключ `QuickSearchParamPrefix + gridId`, значение — имена колонок через запятую). Пустая строка — осознанный отказ от поиска.
4. **Фильтр по типу**: только типы с прямым значением в источнике (Number, Text, Date, Link, DateTimeLocal, LimitedText, TimeLocal). Справочные и вычисляемые исключены.

### Конфигурация

```json
"ClayGrid": {
  "Dynamic": {
    "QuickSearchParamPrefix": "qks"
  }
}
```

Префикс по умолчанию — `"qks"`. Ключ параметра = префикс + ID грида (≤ 20 символов).

### Диалог настройки колонок

Параметр `ShowQuickSearch` (по умолчанию `false`) добавляет колонку с переключателями `MudSwitch`. Включается только для динамического грида с `SupportsQuickSearch=true`. Для недопустимых типов — заблокированная иконка с тултипом «Поиск недоступен для колонок этого типа». Кнопка `SearchOff` сбрасывает флаги на админские значения.

### Видимость поля поиска

Поле скрыто, если итоговый набор колонок пуст (нет админских `1`, пользователь снял все галки, все колонки недопустимого типа). При опустошении набора с непустой строкой поиска — строка очищается. Смена набора при пустой строке не вызывает перезагрузку данных.

## Стилизация панелей

Панели группировки и фильтрации имеют идентичное визуальное оформление. Все цвета используют MudBlazor-переменные палитры (`--mud-palette-*`) — автоматически адаптируются к светлой/тёмной теме. CSS определён в `wwwroot/css/app.css`.

| Элемент | CSS-класс | Свойства |
|---|---|---|
| Панель группировки | `.grouping-tray` | `border-left: 3px solid var(--mud-palette-primary)`, `border-bottom: 2px solid var(--lh-gold)`, фон `var(--mud-palette-background-gray)`, обводка `var(--mud-palette-lines-default)` |
| Панель фильтрации | `.filter-tray` | **Идентично** `.grouping-tray` |
| Иконка (неактивна) | `.grouping-tray-icon`, `.filter-tray-icon` (на `ClayButton` / `MudIconButton`) | `color: var(--mud-palette-text-secondary)`, `opacity: 0.45` |
| Иконка (активна) | `.grouping-tray:has(.grouping-chip) .grouping-tray-icon`, `.filter-tray:has(.filter-chip) .filter-tray-icon` | `color: var(--mud-palette-primary)`, `opacity: 1` — когда в трее есть хотя бы один чип. `ClayButton` применяет класс к внутреннему `MudIconButton`, селекторы продолжают работать |
| Hover / drag-over | `:has(...:hover)`, `.drag-over` | `background: var(--mud-palette-surface)`, `border-left-color: var(--lh-gold)` |
| Чип группировки | `.grouping-chip` | `background: var(--lh-navy)`, белый текст, `border-bottom: 2px solid transparent`; hover: фон `#0A1D6B` + золотой border-bottom |
| Чип фильтрации | `.filter-chip` | **Идентично** `.grouping-chip` (сплошной navy фон, hover с золотым подчёркиванием), но `cursor: default` |

## Групповые операции

Меню групповых операций открывается кнопкой `PlaylistAddCheck` при `SelectVisible="true"`.

### Стандартные операции

Включаются флагами без написания кода на странице:

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `ShowPrint` | `bool` | `false` | Группа «Печать»: текущая страница (реализована), все данные (реализована), выбранные (заглушка) |
| `ShowExcel` | `bool` | `false` | Группа «Выгрузка в Excel»: текущая страница, выбранные, все данные. На время экспорта рядом с заголовком грида показывается `MudProgressCircular` |

```razor
<ClayGrid TEntity="IClayGridRow" Options="_gridOptions" ... />
```

#### Экспорт в Excel

Реализован через два компонента:
- **`ClayGridExcelGenerator`** (сервер) — генерирует .xlsx через ClosedXML в цветах Clayzor (navy `#05164D`, gold `#FFAD00`), шрифт Verdana. Поддерживает: заголовок, описание фильтров/группировки, групповые строки с Excel Outline (вложенные группы через стек по глубине — все уровни имеют +/- контролы), авто-ширину колонок. Групповые строки выводятся без иконок выбора
- **`clayGridExcel.js`** (клиент) — скачивает файл через Blob URL из base64-контента

Поток: `ExcelCurrentPageInternal()` / `ExcelAllInternal()` / `ExcelSelectedInternal()` → `DataLoader.ExcelExportAsync(ExcelExportRequest)` → `ClayGridExcelGenerator.ExportToExcel(...)` → base64 → `clayGridExcel.downloadFile()` → снекбар.

**Индикатор загрузки**: флаг `_isExporting` управляет общим оверлеем `.clay-grid-busy` через `RunBusyAsync(label, work)` (GB3). Единый механизм для экспорта, печати и загрузки данных в динамическом режиме (GB12): затемнение грида + `MudProgressCircular` + подпись.

Все три режима (`CurrentPage`, `Selected`, `All`) полностью реализованы.

#### Печать всех данных

Реализована через три компонента:
- **`ClayGridPageBase.BuildPrintHtmlAsync()`** (сервер) — загружает все строки в отдельный список (НЕ модифицирует `_rows`) и генерирует HTML
- **`ClayGridPrintHtmlGenerator.Build()`** (сервер) — строит самодостаточный HTML с инлайн-стилями (Ч/Б-таблица, тёмный header, `@page{landscape;15mm}`, `thead{display:table-header-group}`). Групповые строки выводятся без иконок разворота/сворачивания и выбора — только имя группы и количество
- **`clayGridPrint.printHtml(html)`** (клиент) — создаёт скрытый iframe, пишет HTML, вызывает `iframe.contentWindow.print()`, удаляет iframe после `afterprint`

Поток: `PrintAllInternal()` → `DataLoader.BuildPrintHtmlAsync(columns, title, ...)` → `ClayGridPrintHtmlGenerator.Build()` → `clayGridPrint.printHtml(html)`.

**Ключевое отличие от v1**: грид (`_rows`, `_dataKey`, `_query`, `ExpandedGroups`) полностью не затрагивается. Печать изолирована в iframe — никакого восстановления страницы не требуется.

**Индикатор загрузки**: единый оверлей `.clay-grid-busy` через `RunBusyAsync` (см. «Экспорт в Excel»).

**Плоский режим**: SQL `SELECT * FROM (selectSql) _src WHERE ... ORDER BY ...` — без `ROW_NUMBER()`.

**Режим группировки**: `WalkTree` с `pageStart=1, pageEnd=int.MaxValue` — всё дерево. Для развёрнутых листовых групп загружаются ВСЕ detail-строки. Свёрнутые группы — только заголовок.

**Режим «Выбранные»**: реализован через `BuildAllRowsForSelected`. В grouped-режиме: C# interleaving по групповым ключам + пост-обработка для подсчёта `SelectedItemCount` на каждый `GroupHeaderRow`.

### Кастомные операции

Параметр `CustomBatchGroups` (`IReadOnlyList<BatchOperationGroup>?`) — список кастомных групп, рендерятся после стандартных.

**`BatchOperationGroup`**:

| Свойство | Тип | Описание |
|---|---|---|
| `Label` | `string` | Заголовок группы |
| `Icon` | `string?` | Иконка Material Icons, опционально |
| `Operations` | `IReadOnlyList<BatchOperation>` | Список операций |

**`BatchOperation`**:

| Свойство | Тип | Описание |
|---|---|---|
| `Label` | `string` | Название |
| `Icon` | `string?` | Иконка, опционально |
| `RequiresSelection` | `bool` | Показывать только при выбранных строках |
| `RequiresAll` | `bool` | Показывать когда ничего не выбрано ИЛИ выбраны все |
| `OnExecute` | `Func<Task>?` | Обработчик (выполняется в приложении) |

```razor
@code {
    private static readonly IReadOnlyList<BatchOperationGroup> MyGroups = new[]
    {
        new BatchOperationGroup
        {
            Label = "Мои операции",
            Icon = Icons.Material.Filled.Settings,
            Operations = new[]
            {
                new BatchOperation
                {
                    Label = "Отправить выбранные",
                    Icon = Icons.Material.Filled.Send,
                    RequiresSelection = true,
                    OnExecute = async () => { /* ... */ }
                },
                new BatchOperation
                {
                    Label = "Архивировать всё",
                    RequiresAll = true,
                    OnExecute = async () => { /* ... */ }
                }
            }
        }
    };
}
```

## Диалог настройки колонок

Кнопка `ViewColumn` в тулбаре открывает `ClayColumnSettingsDialog` — диалог управления порядком, видимостью, фильтром по значению и сортировкой колонок.

Каждая строка чипа содержит:
- **Drag-handle** (`DragIndicator`) — перетаскивание для изменения порядка
- **Название колонки** — кликабельно для циклического переключения сортировки (нет → ASC → DESC → нет), до 2 колонок
- **Переключатель видимости** (`MudSwitch`, `Color.Primary`) — показать/скрыть колонку. Заблокирован для сгруппированных колонок
- **Переключатель фильтра по значению** (`MudSwitch`, `Color.Primary`) — включить/выключить `AllowValueFilter` для колонки. Отображается только при `ShowSorting=true` (скрыт в режиме печати/экспорта)

**Sticky-заголовок** над списком: иконки `Visibility` (видимость) и `Checklist` (фильтр по значению) в контейнерах, соответствующих по ширине `MudSwitch`. При прокрутке длинного списка заголовок прилипает сверху.

### Drag-and-drop

Реализован как jQuery UI Sortable (нативные события `mousedown`/`mousemove`/`mouseup`, **не** HTML5 drag):
- **Ghost** (`.column-settings-ghost`) — клон чипа на `position:fixed`, следует за курсором
- **Placeholder** (`.column-settings-placeholder`) — dashed gold border на месте вставки, динамически вставляется в DOM
- **Авто-прокрутка** — при переполнении контейнера и приближении курсора к верхнему/нижнему краю (зона 40px) контейнер автоматически прокручивается. Скорость растёт пропорционально близости к краю (до 15px/фрейм). Ищется ближайший скроллируемый предок (`getScrollParent`), поскольку прокрутка находится на `DialogContent`, а не на контейнере чипов
- JS-логика в `clayColumnSettings.js` (RCL), результат передаётся в C# через `[JSInvokable] OnJsDrop(sourceIdx, targetIdx)`

### Сортировка

Каждый чип позволяет настроить сортировку по колонке. Клик по области названия колонки или бейджа сортировки циклически переключает состояние: **нет сортировки → ASC (↑) → DESC (↓) → нет сортировки**.

- **До 2 колонок** в сортировке одновременно (приоритеты 1 и 2)
- **Бейдж сортировки** (`.chip-sort-badge`) — золотой фон, navy текст: `1↑` / `2↓`. Отображается справа от названия колонки. Идентичен бейджу в трее группировки
- **Курсор** `pointer` на всей зоне «название + бейдж» (`.sort-toggle-area`)
- **Область сортировки изолирована от drag-and-drop** — JS игнорирует `mousedown` внутри `.sort-toggle-area` и `.chip-label-clickable`
- **Применяется вместе с другими изменениями** при нажатии «Применить»: поля `SortPriority` и `IsSortDesc` на `ColumnSettingsItem` передаются обратно в грид

### Кнопки сброса

Две кнопки в левой части `DialogActions`:

| Кнопка | Иконка | Действие |
|---|---|---|
| «Сбросить сортировку» | `ClearAll` | Очищает `_dialogSortState` — все бейджи исчезают, сортировка возвращается к умолчанию |
| «Восстановить порядок и видимость по-умолчанию» | `RestartAlt` | Возвращает `_items` к снапшоту, сделанному при открытии диалога. Сбрасывает и сортировку, и порядок, и видимость |

### CSS-классы

| Класс | Описание |
|---|---|
| `.column-settings-chip` | Чип: navy фон, белый текст, `border-left: 3px solid transparent`; hover → золотой border-left |
| `.column-settings-ghost` | Клон, следующий за курсором: `opacity:0.88`, `box-shadow`, золотой border-left |
| `.column-settings-placeholder` | Маркер позиции вставки: `color-mix(in srgb, var(--lh-gold) 12%, transparent)`, dashed gold border |

### Применение к гриду

| Возможность | Статус |
|---|---|
| **Видимость** | ✅ MudSwitch → `_hiddenSqlNames` → `ClayColumn.Hidden`. Сгруппированные — переключатель заблокирован |
| **Порядок (диалог→грид)** | ✅ Двухфазный рендеринг: сбор CellTemplate → динамические `TemplateColumn` по `_columnOrder`. Apply → `_dataKey++` → перерендер |
| **Порядок (грид→диалог)** | ✅ `_columnOrder` всегда синхронизирован с DOM (обновляется через `OnColumnDrop`), дополнительное чтение DOM не требуется |
| **Отмена диалога** | ✅ Порядок, видимость и сортировка восстанавливаются из `_columnOrderSnapshot` / `_originalItems` |
| **Сортировка** | ✅ Клик по названию/бейджу → цикл ASC/DESC/нет. До 2 колонок. Бейдж `1↑`/`2↓`. При «Применить» синхронизируется в `_sortState` грида (через `SortName`), вызывается `NotifyQueryChanged()` для перезагрузки данных. Сброс — кнопка `ClearAll` |
| **Header drag** | ✅ Кастомный JS (`clayGridColumnDrag.js`) с **insert**-семантикой (вставка перед/после целевой колонки), заменяет MudBlazor `DragDropColumnReordering` |

### Примечания

- `ClayGrid` требует параметр `Id` — DOM-id корневого элемента (используется `clayGridColumnDrag.init`)
- На странице может быть несколько `ClayGrid` с разными `Id`
- `ClayColumn` регистрирует `CellTemplate` через `IClayGrid.RegisterCellTemplate` (для динамического рендеринга)
- `@onclick` на динамических колонках использует `void HandleSortClick` (избегает async-лямбда-проблем Razor)

## Кастомный drag-and-drop колонок (clayGridColumnDrag.js)

Перемещение колонок в заголовке грида реализовано через кастомный JS (`clayGridColumnDrag.js`) с **insert**-семантикой — перетаскиваемая колонка вставляется перед/после целевой, в отличие от MudBlazor `DragDropColumnReordering`, который делал swap.

### Принцип работы

1. **`clayGridColumnDrag.init(gridId, dotnetRef)`** — вызывается из `OnAfterRenderAsync` ClayGrid при каждом перерендере динамических колонок. Безопасен для многократного вызова (dispose предыдущего).
2. **`dragstart`** (capture:true) — определяет `srcSqlName` по `data-col-sql`, устанавливает `effectAllowed='move'`, вызывает C# `SetDraggedColumn(sql)` → устанавливает `ClayDragState.DraggedColumn` для tray-drop.
3. **`dragover`** (capture:true) — показывает индикатор вставки (`.clay-grid-drop-indicator`) на целевой колонке: слева от центра = вставить перед, справа = после.
4. **`drop`** — вызывает C# `OnColumnDrop(srcSql, targetSql, insertBefore)` → обновляет `_columnOrder` через insert (удаление источника + вставка на целевую позицию) → `_dataKey++` → перерендер. В динамическом режиме сразу сохраняет новый порядок в БД через `SaveDynamicState()` (без перезагрузки данных — двигаются только столбцы).
5. **`dragend`** — очистка, вызов `SetDraggedColumn(null)`.
6. **`clayGridColumnDrag.dispose(gridId)`** — в `DisposeAsync` ClayGrid для очистки обработчиков.

### Требования

- Заголовки колонок должны содержать `data-col-sql` с SQL-именем
- `DragAndDropEnabled="false"` на всех `TemplateColumn` — MudBlazor не участвует в drag-and-drop колонок
- `ClayGrid` должен иметь уникальный `Id` (используется для поиска корневого элемента)
- `App.razor` должен подключать JS: `<script src="_content/Clayzor.Lib.Web.Controls/js/clayGridColumnDrag.js"></script>`
- CSS-индикатор (`.clay-grid-drop-indicator`) определён в `app.css`

### ClayGrid → JS-методы

| JSInvokable | Направление | Описание |
|---|---|---|
| `SetDraggedColumn(string?)` | JS → C# | Устанавливает/сбрасывает `ClayDragState.DraggedColumn` |
| `OnColumnDrop(src, target, insertBefore)` | JS → C# | Применяет insert-перемещение в `_columnOrder`; в динамике сохраняет порядок в БД |
