# ClayGrid — компонент грида

> Глобальные правила — в `/AGENTS.md`. Контекст библиотеки — в [`../AGENTS.md`](../AGENTS.md).

## Динамический режим (DynamicGrid)

Пакет `Components/Grid/Dynamic/` — конфигурация и DI для динамического режима, в котором грид читает определение
(SQL, колонки, кнопки) из БД. План реализации: [promts/_done/DynamicFilter/_readme_grid_dynamic.md](promts/_done/DynamicFilter/_readme_grid_dynamic.md).
Выполненные промты (G0–G14, GF1–GF16, GG1–GG9, GN1–GN4, GE1–GE6, GB1–GB17, TG1–TG9): `promts/_done/DynamicFilter/`.
Выполненные промты быстрого поиска (QS0–QS9): `promts/_done/QS/`. Оркестратор: `promts/_done/QS/QS0_README_quick_search.md`.
Отложенные промты: `promts/_later/`.
Выполненные промты групповых операций (CGO, CGR1): `promts/_done/CGO/`, `promts/_done/CGR/`.

Модели данных (`ClayGridSchemaMap`, `ClayGridDefinition`, `ClayColumnDefinition`) и классы доступа к БД
(`ClayGridDefinitionData`, `DynamicSql`) живут в **`Clayzor.Lib.Entities.DynamicGrid`** — см. [../Clayzor.Lib.Entities/AGENTS.md](../../../Clayzor.Lib.Entities/AGENTS.md).

## Карта

| Что | Где |
|---|---|
| Документация ClayGrid | [docs/clay-grid.md](../docs/clay-grid.md) |
| Документация ClayEditForm | [docs/clay-edit-form.md](../docs/clay-edit-form.md) |
| Документация ClayComboBox | [docs/clay-combo-box.md](../docs/clay-combo-box.md) |
| Документация ClayErrorBar | [docs/clay-error-bar.md](../docs/clay-error-bar.md) |
| Документация ClayColumnFilterDialog | [docs/clay-column-filter-dialog.md](../docs/clay-column-filter-dialog.md) |
| Документация ConfirmDialog | [docs/confirm-dialog.md](../docs/confirm-dialog.md) |
| План динрежима | [promts/_done/DynamicFilter/_readme_grid_dynamic.md](promts/_done/DynamicFilter/_readme_grid_dynamic.md) |
| Багфиксы динрежима (GF) | [promts/_done/DynamicFilter/GF0_README_dynamic_fixes.md](promts/_done/DynamicFilter/GF0_README_dynamic_fixes.md) |
| Группировка динрежима (GG) | [promts/_done/DynamicFilter/GG0_README_dynamic_grouping.md](promts/_done/DynamicFilter/GG0_README_dynamic_grouping.md) |
| Снятие потолка уровней (GN) | [promts/_done/GN/GN0_README_grouping_levels.md](promts/_done/GN/GN0_README_grouping_levels.md) |
| Печать и Excel (GE) | [promts/_done/GE/GE0_README_dynamic_export.md](promts/_done/GE/GE0_README_dynamic_export.md) |
| UX-багфиксы (GB) | [promts/_done/DynamicFilter/GB0_README_grid_ux_fixes.md](promts/_done/DynamicFilter/GB0_README_grid_ux_fixes.md) |
| Быстрый поиск (QS) | [promts/_done/QS/QS0_README_quick_search.md](promts/_done/QS/QS0_README_quick_search.md) |
| Рефакторинг опций (CGO) | [promts/_done/CGO/CGO0_README_grid_options.md](promts/_done/CGO/CGO0_README_grid_options.md) |
| «Поделиться» (SH) | [promts/_done/SH_share/SH0_README_share.md](promts/_done/SH_share/SH0_README_share.md) |
| Аудит безопасности (GA0–GA9) | [promts/GA0_README_audit_grid.md](promts/GA0_README_audit_grid.md) |
| GA1 — ORDER BY / GROUP BY whitelist (выполнен) | [promts/GA1_orderby_groupby_whitelist.md](promts/GA1_orderby_groupby_whitelist.md) |
| GA2 — детерминированные имена параметров (выполнен) | [promts/GA2_group_param_names.md](promts/GA2_group_param_names.md) |
| GA3 — экранирование разделителей (выполнен) | [promts/GA3_state_serializer_escaping.md](promts/GA3_state_serializer_escaping.md) |
| GA4 — сброс состояния перед shared (выполнен) | [promts/GA4_shared_reset_and_async.md](promts/GA4_shared_reset_and_async.md) |

## Модели и классы

| Класс | Назначение |
|---|---|
| `ClayGridDynamicSettings` | Настройки динрежима: имена таблиц (`SettingsTable`, `ColumnsTable`, `UserParamsTable`, `UserSharedParamsTable`), имя табличной функции/процедуры `UserParamsShared`, префиксы query-параметров, `ConnectionStringName`, `QuickSearchParamPrefix`. Связывается из `"ClayGrid:Dynamic"` через `IOptions<T>`. `Validate()` проверяет обязательные поля |
| `ClayGridParamRegistry` | Единый реестр имён параметров грида (SH4): `GetGridParamNames(settings, gridId)` → 6 имён (cols/filter/group/sort/pageSize/quickSearch префикс + gridId). Проверка длины ≤ 20 с именем свойства в ошибке. Чистая функция |
| `ClayShareUrlBuilder` | Построитель URL для «Поделиться» (SH6): `BuildShareUrl(currentUrl, gridIdParam, sharedId)` — белый список параметров (только gridId + sharedId), абсолютный URL, URL-кодирование. Чистая функция, 5 тестов |
| `ClaySharedParamValidator` | Валидатор имён параметров общей настройки (SH8): `IsValid(sharedNames, gridParamNames)` — строгая проверка (подмножество ОК, чужое имя → false). Сравнение `OrdinalIgnoreCase`. Чистая функция, 7 тестов |
| `ClayColumnKind` | Enum типов колонок (1–13): Number=1, Text=2, Date=3, Link=4, List=5, ConditionBool=6, Bool=7, Html=8, Icon=9, DateTimeLocal=10, ConditionList=11, LimitedText=12, TimeLocal=13 |
| `ClayColumnKindExtensions` | `SupportsQuickSearch(int kind)` — белый список типов, допустимых для быстрого поиска (1,2,3,4,10,12,13). Исключены справочные (5,9), фильтр-онли (6,11), булевы (7), HTML (8) |
| `ClayColumnTypeMap` | `Resolve(int)` → существующий `ColumnTypeDescriptor` (1→Number, 2→Text, 3→Date, 4→Text, 7→Boolean); `IsSupported(int)` |
| `ClayColumnFormat` | `Parse(int, string?)` — разбор строки `Формат` из БД |
| `ClayGridLinkResolver` | `Resolve(string?, IConfiguration?)` — резолвинг URL из определения: null/пусто, `@Key` из конфигурации, прямые URL |
| `GridStateSerializer` | Сериализация/десериализация состояния грида (колонки, сортировка, группировка, фильтр JSON, размер страницы). Чистые функции |
| `ClayGridUrlFilterParser` | Разбор URL-фильтра `КлючURL=op~value`: `ParsedUrlFilter` record, `Parse` (правила 1/2/5), `Apply` (слияние в дерево с учётом сохранённых параметров) |
| `ClayHtmlSanitizer` | `Sanitize(html)` — вырезает `<script>`, `onXxx`-атрибуты, `javascript:` |
| `ClayDateTimeLocalColumnType` | Дескриптор Тип 10: дата-время из UTC в локальный пояс. Формат = .NET-строка (напр. `"dd.MM.yyyy HH:mm"`). Фильтруется как Date |
| `ClayTimeLocalColumnType` | Дескриптор Тип 13: время из UTC в локальный пояс. Формат = .NET-строка (напр. `"HH:mm"`). Фильтруется как Date |
| `ClayDateTimeConverter` | Статический конвертер: `ConvertFromUtc(DateTime?, TimeSpan)` и `Format(object?, string?, TimeSpan?)`. Чистые функции (без DateTime.Now), тестируемо |
| `ClayDynamicRow` | Строка динамического грида. Реализует `IClayGridRow` + `IDetailRow` + `IReadOnlyDictionary<string, object?>`. `IDetailRow.Item => this` — строка сама является словарём для `GetRowIdValue`. Заменяет `InvalidCastException`-каст в `LoadDynamicData` |
| `ClayGroupRowMapper` | Статический маппер словарей агрегатного GROUP BY в `GridGroupRow` для `ClayGroupingEngine`. `MapRow(row, levelCount)` / `MapRows(rows, levelCount)`, нормализация DBNull. Общий для статического и динамического режимов. Чистые функции. Заменил `ClayDynamicGroupMapper` (GN1) |
| `ClayDynamicCellReader` | Реализация `IClayGridCellReader` для динамических строк-словарей. Типы 1/2/3/7 сырыми, 5/9 через справочники, 10/13 со смещением, 8 StripHtml, 12 полный текст. Без БД (GE2) |
| `Services/ServiceCollectionExtensions.AddClayGridDynamic()` | Регистрирует `ClayGridDynamicSettings` в DI + валидатор `IValidateOptions<T>`. Там же `AddClayTree()` |

## Codebehind-структура ClayGrid

После рефакторинга логика `ClayGrid<TEntity>` разнесена по partial-файлам. Все файлы объявляют `public partial class ClayGrid<TEntity> where TEntity : class` в namespace `Clayzor.Lib.Web.Controls.Components.Grid`. Базовый класс (`ComponentBase`) и реализуемые интерфейсы (`IClayGrid`, `IDisposable`, `IAsyncDisposable`) — только в основном файле.

| Файл | Строк | Содержание |
|---|---|---|
| `ClayGrid.razor` | ~640 | Разметка грида (MudDataGrid, тулбар, панели, колонки) |
| `ClayGrid.razor.cs` | ~540 | Основа: интерфейсы, параметры, поля, инициализация, регистрация колонок, `NotifyQueryChanged`, высота грида, `DisposeAsync`, `OpenColumnSettings`, `BuildColumnSettingsItems` |
| `ClayGridOptions.cs` | ~130 | Настройки одного экземпляра грида (`ClayGridOptions`): 21 свойство, `Defaults`, серия CGO |
| `ClayGrid.Search.cs` | 18 | `_searchText`, `DebounceTimer`, обработчики поиска |
| `ClayGrid.Sorting.cs` | 66 | `_sortState`, `ToggleSort`, `HandleSortClick`, `GetSortBadge` |
| `ClayGrid.Grouping.cs` | ~250 | `OnGroupToggle`, `GroupRowHostKey`, `IsGroupRowHost`, `_groupColumns`, `_trayExpanded`, группировка, `_groupChildIds` |
| `ClayGrid.Filtering.cs` | ~420 | `_filterRoot`, `HasComposite`, `ValueFilterLeaves`, `OpenFilterDialog`, `OpenValueFilterDialog`, чипы, фильтр-трей |
| `ClayGrid.DragDrop.cs` | 86 | `_dragSourceIndex`, drag-and-drop чипов группировки |
| `ClayGrid.Selection.cs` | 113 | `_selectMode`, `_selectAllChecked`, `_selectedIds`, персистентность выделения |
| `ClayGrid.ExportMenu.cs` | ~240 | `_isExporting`, `ResolveExportColumnsAsync`, печать и экспорт |
| `ClayGrid.Paging.cs` | 59 | `_pageSize`, `OnPageSizeChanged`, `PrevPage`, `NextPage`, `LastPage` |
| `ClayGrid.Dynamic.cs` | ~120 | Динамический режим: инжекты, `InitDynamicMode`, `LoadDynamicData`, `ResolveDynamicGridId` |
| `ClayGrid.Dynamic.Export.cs` | ~200 | Загрузка строк для экспорта в динрежиме (GE3) |

### Codebehind-структура ClayGridPageBase

Логика `ClayGridPageBase<T>` разнесена по partial-файлам. Все файлы объявляют `public abstract partial class ClayGridPageBase<T> where T : Entity` в namespace `Clayzor.Lib.Web.Controls.Components.Grid`. Базовый класс (`ComponentBase`) и реализуемые интерфейсы (`IClayGridDataLoader`) — только в основном файле.

| Файл | Строк | Содержание |
|---|---|---|
| `ClayGridPageBase.cs` | ~530 | Ядро: `[Inject]`-сервисы, `Grid`, `LoadData`, `LoadFlatData`, `LoadGroupedData`, `LoadDistinctValuesAsync`, `IClayGridDataLoader` |
| `ClayGridPageBase.ColumnTypes.cs` | 83 | Вывод типов колонок: `_idColumnName`, `_propertyMap`, `_inferredColumnTypes`, `FilterColumnTypes` |
| `ClayGridPageBase.Export.Excel.cs` | 208 | Экспорт в Excel: `IClayGridDataLoader.ExcelExportAsync`, `BuildAllRowsForExcel` |
| `ClayGridPageBase.Export.Print.cs` | 89 | Печать всех данных: `BuildAllRowsForPrint` |
| `ClayGridPageBase.Export.Selected.cs` | 225 | Экспорт/печать выбранных: `BuildPrintHtmlForSelectedAsync`, `BuildAllRowsForSelected` |

## Server-side grouping architecture

Группировка выполняется **на стороне SQL Server** двумя отдельными запросами. Реализация — `ClayGroupingEngine` (статический класс в `Components/Grid/ClayGroupingEngine.cs`).

1. **Запрос групповых агрегатов** — `GROUP BY` + `COUNT(*)`, возвращает уникальные значения группировки и количество записей
2. **Запрос детальных строк** — выборка конкретных записей с `ROW_NUMBER()` и фильтром по значениям группы

### Модель данных
- `IClayGridRow` — маркерный интерфейс строки в плоском списке
- `GroupHeaderRow` — заголовок группы: `FullKey`, `DisplayValue`, `ItemCount`, `Depth`, `IsExpanded`
- `DetailRow<T>` — обёртка сущности: `Item`, `GroupKey`, `Depth`
- `GroupedPage<T>` — результат запроса: `Rows` + `TotalEffectiveRows`
- `ClayDataQuery.ExpandedGroups` — `HashSet<string>` полных ключей развёрнутых групп

### Пагинация с группами
- Каждый заголовок группы = 1 эффективная строка, каждая строка детализации = 1
- `TotalCount` = общее эффективное количество строк
- При сворачивании/разворачивании группы количество видимых строк меняется, страница пересчитывается

### Многоуровневая группировка
- SQL: `GROUP BY Col1, Col2, ...` — возвращает листовые агрегаты. Число уровней не ограничено (GN1).
- `GridGroupRow.Keys` — список значений группировочных колонок (N уровней, GN1). `null` — законное значение ключа.
- C#: синтетические родительские узлы создаются из листовых, `ItemCount` родителя = сумма дочерних.
- `ItemCount` учитывается только для листовых узлов (`Children.Count == 0`).

### Имена колонок в WHERE и GROUP BY
- `SearchColumns` — выходные имена, видны в подзапросе `ROW_NUMBER()`.
- `GroupColumns` содержат те же выходные имена — напрямую в `GROUP BY`.
- `ClayGridPageBase` читает `SearchColumns`, `SelectSql`, `DefaultOrder` из `Grid` (реализация `IClayGrid`).

## Server-side column filtering

Фильтрация по колонкам выполняется **на стороне SQL Server** через `ClayCompositeSqlBuilder.Build`.
Единый источник истины — дерево `ClayFilterGroupNode`. UI — панель фильтров (filter tray) с drag-and-drop заголовков и диалогом `ClayColumnFilterDialog`.

### Модель данных
- `ColumnType` — тип данных колонки: `Text` (10 операторов), `Number`, `Boolean`
- `ColumnFilterOperator` — оператор сравнения (Contains, Equals, GreaterThan, …)
- `LogicalOperator` — `And` / `Or` для объединения узлов в группе
- `ColumnFilter` — условие фильтра: `Column`, `ParamName`, `Operator`, `Value` + опциональный второй клауз. Реализует `IClayFilterNode`
- `ClayFilterGroupNode` — группа И/ИЛИ (`LogicalOperator Logic` + `List<IClayFilterNode> Nodes`)
- `ClayDataQuery.CompositeFilter` — `ClayFilterGroupNode?` — единый источник истины фильтрации

### SQL-генерация
- `ClayCompositeSqlBuilder.Build(root, parameters, knownColumns, columnNameMap?)` — рекурсивно обходит дерево и возвращает фрагмент WHERE. Имя колонки — только из белого списка; значения — только Dapper-параметры
- `ClayGridPageBase.BuildCompositeFilterClause(CompositeFilter?, dp, columnNameMap?)` — обёртка над билдером

### Типы составного фильтра (`Components/Filter/`)
- `IClayFilterNode` — интерфейс узла дерева фильтра: `Clone()` (рекурсивное глубокое копирование)
- `ClayFilterGroupNode` — группа И/ИЛИ с рекурсивным `Clone()`
- `ColumnFilter` — листовой узел дерева. `Source` (`ColumnDialog` / `CompositeDialog`)
- `ValueFilter` — листовой узел: фильтрация по набору выбранных значений (Excel-style). Поля: `Column`, `Values`, `Negate`, `BlankChecked`, `ParamPrefix`
- `ClayCompositeSqlBuilder.Build` для `ValueFilter`: IN/NOT IN с учётом Negate×BlankChecked, 6 комбинаций

### Сериализация и URL-персистенция фильтра
- `ClayFilterJsonConverter : JsonConverter<IClayFilterNode>` — полиморфная JSON-сериализация с дискриминатором `$type`. Транзиентные поля — `[JsonIgnore]`
- `ClayFilterUrlHelper` — дерево → JSON → `DeflateStream` → Base64Url (и обратно). Query-параметр `filter`
- Восстановление при загрузке: `ClayGridPageBase.OnAfterRenderAsync(firstRender)` читает `filter` из URL, десериализует и вызывает `Grid.RestoreFilter(root)`

## Выполненные шаги

### Динрежим (G0–G14)
- G0 — `scripts/dynamic-grid/schema.sql`: 3 таблицы + триггер-upsert + сид #140
- G1 — опции, схема, DI, тесты TG1
- G1b — `DynamicSql` в `Clayzor.Lib.Entities.DynamicGrid`
- G2 — модели (`ClayGridDefinition`, `ClayColumnDefinition`), `ClayGridDefinitionData`, `ClayGridSchemaMap` в Entities
- G3 — `ClayColumnKind`, `ClayColumnTypeMap`, `ClayColumnFormat`
- G4 — `ClayGrid.Dynamic.cs`: динамический рендер, загрузка определения/колонок/данных
- G5 — `ClayGridLinkResolver`, кнопки действий в динамическом режиме
- G6 — `ClayGridUserParamsData`: сохранение/чтение пользовательских параметров
- G7 — `GridStateSerializer`, сохранение/восстановление состояния
- G8 — `ClayGridUrlFilterParser`, разбор URL-фильтра
- G9 — URL-параметр `cols` (видимость колонок)
- G10 — `ClayListColumnType` (Тип 5): справочник через подзапрос
- G11 — `ClayIconColumnType` (Тип 9): `<img>` с tooltip
- G12 — `ClayConditionBoolColumnType` (Тип 6) и `ClayConditionListColumnType` (Тип 11): фильтр-онли
- G13 — `ClayHtmlSanitizer`, `ClayHtmlColumnType` (Тип 8), `ClayLimitedTextColumnType` (Тип 12), Тип 4
- G14 — `ClayDateTimeLocalColumnType` (Тип 10), `ClayTimeLocalColumnType` (Тип 13), `ClayDateTimeConverter`

### Багфиксы динрежима (GF)
- GF1–GF6, GF8–GF13, GF16 — различные исправления динамического режима

### Группировка (GG)
- GG1–GG9 — маппинг, конвейер, раскрытие/сворачивание, чипы лотка, tri-state чекбоксы групп, диалог настройки

### Снятие потолка уровней (GN)
- GN1–GN4 — `GridGroupRow.Keys`, N уровней, `BuildGroupKeyWhere`, C#-interleaving экспорта

### Печать и Excel (GE)
- GE1–GE6 — `IClayGridCellReader`, динамическая реализация, загрузка строк, печать, Excel, включение

### UX-багфиксы (GB)
- GB1–GB19 — различные UX-исправления

### Рефакторинг (CGO/CGR)
- CGO — сведение параметров `ClayGrid` в `ClayGridOptions` (21 параметр → POCO)
- CGR1 — переименование `ClayGridDynamicOptions` → `ClayGridDynamicSettings`

### «Поделиться» (SH2–SH9)
- SH2–SH9 — схема БД, конфигурация, реестр имён, кнопка «Поделиться», URL, список настроек, режим sharedId

### Аудит безопасности (GA)
- GA0 — сводка 14 находок, план исправлений (9 промтов)
- GA1 — ✅ валидация ORDER BY / GROUP BY против белого списка `_columnBySqlName` (defence-in-depth: `BuildOrderBy` + `DeserializeSort` + `ApplySavedSort` / `ApplySavedGroups`)
- GA2 — ✅ детерминированные индексы групп вместо `string.GetHashCode()` в именах Dapper-параметров
- GA3 — ✅ экранирование разделителей `,` `:` `%` в `GridStateSerializer` (процентное кодирование, `LastIndexOf` вместо `Split`)
- GA4 — ✅ сброс личного состояния к дефолтам перед применением shared-настроек (`ApplySharedParams` async, `ResetColumnsToDefinitionDefault`, `await RefreshQuickSearchEffective`)
- GA5–GA9 — ожидают выполнения
