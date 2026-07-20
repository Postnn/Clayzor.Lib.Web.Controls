> Глобальные правила и обзор решения — в корневом /AGENTS.md. Здесь — только специфика проекта Clayzor.Lib.Web.Controls.

## Shared components (Clayzor.Lib.Web.Controls)

| Компонент | Документация |
|---|---|
| **ClayGrid\<T>** — грид с серверной пагинацией, поиском, сортировкой, группировкой, фильтрацией по колонкам. Разметка в `ClayGrid.razor`, логика в 9 partial class-файлах (1 основной + 8 по темам, см. «Codebehind-структура» ниже). При `EditDialogType != null` автоматически добавляет сервисную колонку (первой) с иконкой карандаша для открытия диалога редактирования. Конфигурация передаётся через параметры: `SelectSql`, `SearchColumns`, `DefaultOrder`, `EditDialogType`, `DataLoader`, `ColumnMenuMode`. `OnGroupToggle` — страница подписывается на раскрытие/сворачивание групп (грид сам рендерит `ClayGroupHeader` в вычисленной хост-колонке `GroupRowHostKey`)| [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGridPageBase\<T>** — базовый класс страниц с гридом в 5 partial-файлах (1 основной + 4 по темам, см. «Codebehind-структура ClayGridPageBase» ниже). Читает конфигурацию SQL из `Grid` (IClayGrid). Предоставляет `LoadData`, `ToggleGroup`, `OpenAddDialog`. Авто-вычисляет `FilterColumnTypes` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumn\<T>** — колонка грида с авто-заголовком. Получает Title/SortName/Drag&Drop из `ClayColumnDef` по `ColumnId`. Скрывается при группировке. Кнопка меню ⋮ для мобильных | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumnDef** — невидимый регистратор метаданных колонки: `ColumnId` (EditorRequired), `SqlName`, `DisplayName`, `SortName`, `Groupable`, `Filterable`, `AllowValueFilter`, `BoolTrueLabel`, `BoolFalseLabel` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGroupHeader** — заголовок строки группы с иконкой раскрытия/сворачивания и количеством элементов. Рендерится гридом автоматически в вычисленной хост-колонке (`GroupRowHostKey`) — страница напрямую компонент не вызывает, только подписывается на `OnGroupToggle` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayDragState** — статическое хранилище SQL-имени перетаскиваемой колонки между dragstart и drop | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGridPrintHtmlGenerator** — статический генератор HTML для печати всех данных грида. Генерирует HTML с теми же MudBlazor CSS-классами (`.mud-table`, `.mud-table-cell`, `.group-header-cell`) и встраивает полный `@media print` CSS — визуально идентичен печати текущей страницы | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGridPrintStyles** — символы для печатных форм: иконки групп (`▸`/`▾` — аналог MudBlazor ChevronRight/ExpandMore), булевы значения (`✓`/`✗` — аналог CheckCircle/Cancel) | — |
| **ClayColumnFilterDialog** — диалог настройки фильтра по колонке с типо-зависимыми операторами. Использует `ClayFilterValueEditor` для редакторов значений. Параметр `InitialOperator` позволяет задать начальный оператор для нового фильтра | [docs/clay-column-filter-dialog.md](docs/clay-column-filter-dialog.md) |
| **ClayColumnValueFilterDialog** — диалог фильтра по уникальным значениям (Excel-style): кастомные чекбоксы (как в гриде), «выделить все» (tri-state), контекстные условия через `MudMenu`, ленивая загрузка, обработка порога 100, взаимоисключение с фильтром по условию. Возвращает `ValueFilter`, `Cleared`, `OpenConditionRequest` или `RemoveCondition` | [docs/clay-grid.md](docs/clay-grid.md) |
| **OpenConditionRequest** — record для маршрутизации из диалога значений в форму условия с пресетом оператора | — |
| **ClayFilterOption** — класс варианта для выпадающего списка значения фильтра: `Value` (object?), `Label` (string) | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterValueEditor** — единый редактор значения фильтра по типу колонки (Text/Number/Decimal/Date/Boolean/lookup). Скрывается при операторах без значения. Переиспользуется в `ClayColumnFilterDialog` и `ClayFilterDialog` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterOperatorLabels** — статический хелпер: читаемые русские метки операторов фильтрации. Переиспользуется в `ClayColumnFilterDialog` и `ClayFilterDialog` | — |
| **ClayFilterStrings** — единый источник строковых констант UI фильтра (заголовки, кнопки, подписи). Устраняет хардкод русских строк в разметке диалогов | — |
| **ClayFilterJsonConverter** — `JsonConverter<IClayFilterNode>` с дискриминатором `$type` (`"group"`/`"column"`/`"value"`). Транзиентные поля (`ParamName`, `SecondParamName`, `ParamPrefix`, computed-свойства) исключены через `[JsonIgnore]` | — |
| **ClayFilterUrlHelper** — статический хелпер: дерево → JSON → DeflateStream → Base64Url (и обратно). Query-параметр `filter`. Используется `ClayGridPageBase` для восстановления фильтра при первой загрузке | — |
| **ClayFilterExpression** — редактор одного листового условия составного фильтра. Компактная однорядная раскладка (`flex-wrap:wrap`, `Dense="true"`): Поле / Условие / Значение / ✕. Автофокус на «Значение» после смены колонки/оператора (через `@key` ремоунт + `AutoFocus`). При `Node.IsNew` (условие добавлено перетаскиванием) сразу фокусирует значение | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterGroup** — рекурсивный узел-группа составного фильтра с `MudToggleGroup` И/ИЛИ, кнопками добавления условия/группы (в одной строке с переключателем) и удаления. Корневая группа (`IsRoot=true`) рендерится плоско (без рамки/отступа). Не-корневые — компактно (`gap:6px`, `border-left:2px`). `GetLeafDescription` делегирует в `ClayFilterDescriptionBuilder.DescribeLeaf` (оба клауза). Условия `ColumnDialog` отображаются read-only с кнопкой удаления (крестик) | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterDialog** — диалог настраиваемого (составного) фильтра. Фиксированная высота через скоупленный `ContentClass` (`width:600px; height:min(460px,80vh); overflow:hidden; flex column`). Описание — фиксировано сверху (`flex:0 0 auto`), дерево условий — единственная прокручиваемая зона (`flex:1; overflow-y:auto; min-height:0`). `DragMode=Simple` (перетаскивается). Всегда рендерит корневую группу, работает с глубокой копией дерева, возвращает результат через `DialogResult.Ok(ClayFilterGroupNode)` | [docs/clay-grid.md](docs/clay-grid.md) |
| **FilterSegment** — кликабельный сегмент в панели фильтра: `Text`, `Source` (ColumnDialog/CompositeDialog/ValueFilter), `Column` (маршрутизация клика) | — |
| **DistinctValuesResult** — результат `LoadDistinctValuesAsync`: `Values` (IReadOnlyList<object?>), `Capped` (> лимита 100), `HasBlanks` (есть NULL/пустые), `TotalDistinct` (полное число) | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterDescriptionBuilder** — статический построитель: `BuildSegments(root, getDisplayName, getColumnMeta?)` → список кликабельных сегментов; `BuildText(root, getDisplayName, getColumnMeta?)` → строка описания для экспорта/печати; `DescribeLeaf(leaf, getDisplayName)` → текст одного ColumnFilter с обоими клаузами; `DescribeValueFilter(vf, getDisplayName, getColumnMeta?)` → текст фильтра по значению («одно из [v1, v2]» / «кроме [...]»). V8: поддержка `ValueFilter` в сегментах и тексте, форматирование через `ColumnTypeDescriptor.Format` и `BoolTrueLabel`/`BoolFalseLabel` | — |
| **ClayColumnSettingsDialog** — диалог настройки порядка, видимости, сортировки и фильтра по значению колонок (jQuery UI Sortable drag-and-drop с авто-прокруткой). Параметр `ShowSorting` (default `true`) — скрывает секцию сортировки для режима печати/экспорта. V8: sticky-заголовок с иконками `Visibility`/`Checklist`, переключатель `AllowValueFilter` (только при `ShowSorting`). Валидация: нельзя применить с нулём видимых колонок | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumnSettingsPromptDialog** — лёгкий диалог с тремя исходами перед печатью/экспортом: «Выбрать колонки» (→ диалог настройки), «Как на странице» (→ текущий вид), «Отмена». Параметр `ContextLabel` — контекст операции | — |
| **ClayEditForm\<T>** — MudDialog с валидацией, сохранением, удалением | [docs/clay-edit-form.md](docs/clay-edit-form.md) |
| **ClayComboBox\<TItem>** — выпадающий список для `ILookupEntity`. Рендерит `MudSelect` с `Variant="Variant.Outlined"`, `Margin="Margin.Dense"`, `Dense="true"` и `PopoverClass="clay-combo-popover"`. CSS-правила `.clay-combo-popover` (overflow, max-height, line-height, font-size) живут в `app.css` | [docs/clay-combo-box.md](docs/clay-combo-box.md) |
| **ClayErrorBar** — баннер ошибок БД с детализацией (SQL, параметры) | [docs/clay-error-bar.md](docs/clay-error-bar.md) |
| **ClayButton** — обёртка `MudTooltip` + `MudIconButton` с авто-сбросом тултипа после клика. Заменяет пару `<MudTooltip><MudIconButton/></MudTooltip>` | — |
| **ClayMenu** — обёртка `MudMenu` с авто-построением кнопки-активатора (опциональный тултип, сброс тултипа после клика). Заменяет `<MudMenu><ActivatorContent><MudTooltip><MudIconButton/></MudTooltip></ActivatorContent></MudMenu>` | — |
| **ClayCheckbox** — контролируемый (controlled) чекбокс с tri-state поддержкой (`State`: `true`/`false`/`null`). Кастомный `<span>`-глиф (16×16, CSS-галочка border-rotate). Используется для выбора записей в гриде и фильтра по значению | — |
| **ConfirmDialog** — диалог подтверждения | [docs/confirm-dialog.md](docs/confirm-dialog.md) |
| **ILookupEntity** — интерфейс справочной сущности (`int Id`, `string Name`) | [../Clayzor.Lib.Entities/docs/entity-crud.md](../Clayzor.Lib.Entities/docs/entity-crud.md) |
| **ClayTheme** — corporate theme (DarkNavy + Gold accent). PaletteLight references `ClayColors.*` (single source of truth for brand hex values). Typography references CSS variables `--clay-font-family` (Verdana) and `--clay-font-size` (0.8rem). Applied in MainLayout. **Important**: palette values must be C# hex literals, NOT `var(...)` — MudBlazor parses them via `MudColor.Parse()` | — |
| **ClayColors** — public C# constants (`public const string`) for every brand color. Single source of truth — shared by `ClayTheme.cs` and (via `--mud-palette-*` variables) by `app.css`. See STYLE_RULES.md §1 (Variant A) | — |

### Интерфейсы

| Интерфейс | Назначение |
|---|---|
| **IClayGrid** — контракт ClayGrid: `SelectSql`, `SearchColumns`, `DefaultOrder`, `EditDialogType`, `ColumnMenuMode`, `IsGrouped`, `ToggleSort`, `GetSortBadge`, `GetColumnMeta`, `AddGroupAsync`, `AddFilterAsync`, `IsValueFilterAvailable`, `IsValueFilterActive`, `OpenValueFilterDialog` (V7), регистрация колонок | [docs/clay-grid.md](docs/clay-grid.md) |
| **IClayGridDataLoader** — контракт обратного вызова: `OnQueryChangedAsync(ClayDataQuery)`, `ExcelExportAsync(ExcelExportRequest)`, `BuildPrintHtmlAsync(columns, title, filterDescription, groupDescription)`, `BuildPrintHtmlForCurrentPageAsync(columns, title, filterDescription, groupDescription)`, `BuildPrintHtmlForSelectedAsync(...)`, `LoadDistinctValuesAsync(sqlName, query, limit)` — загрузка уникальных значений колонки для Excel-style фильтра. Реализуется ClayGridPageBase, передаётся через `DataLoader="this"` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumnMeta** — метаданные зарегистрированной колонки: `ColumnId`, `SqlName`, `DisplayName`, `SortName`, `Groupable`, `Filterable`, `AllowValueFilter`, `BoolTrueLabel`, `BoolFalseLabel`, `Type` | [docs/clay-grid.md](docs/clay-grid.md) |
| **IClayGridCellReader** — абстракция чтения ячейки для генераторов печати и Excel. `TryGetCellValue(IDetailRow, ClayColumnMeta, out value, out valueType)`. Реализации: `ClayReflectionCellReader` (статика), `ClayDynamicCellReader` (динамика, GE2) | — |

### DynamicGrid — динамический режим ClayGrid

Пакет `Components/Grid/Dynamic/` — конфигурация и DI для динамического режима, в котором грид читает определение
(SQL, колонки, кнопки) из БД. План реализации: [promts/_done/_readme_grid_dynamic.md](Components/Grid/promts/_done/_readme_grid_dynamic.md).
Выполненные промты (G0–G14, GF1–GF16, GG1–GG9, GN1–GN4, GE1–GE6, TG1–TG9): `promts/_done/`.
Отложенные промты: `promts/_later/`.
Активные багфиксы (GB1–GB11): `promts/GB*.md`. Оркестратор: `promts/GB0_README_grid_ux_fixes.md`.

| Класс | Назначение |
|---|---|
| `ClayGridDynamicOptions` | Настройки динрежима: имена таблиц, префиксы query-параметров, `ConnectionStringName`. Связывается из `"ClayGrid:Dynamic"` через `IOptions<T>`. `Validate()` проверяет обязательные поля |
| `ClayColumnKind` | Enum типов колонок (1–13): Number=1, Text=2, Date=3, Link=4, List=5, ConditionBool=6, Bool=7, Html=8, Icon=9, DateTimeLocal=10, ConditionList=11, LimitedText=12, TimeLocal=13 |
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
| `ServiceCollectionExtensions.AddClayGridDynamic()` | Регистрирует `ClayGridDynamicOptions` в DI + валидатор `IValidateOptions<T>` |

Модели данных (`ClayGridSchemaMap`, `ClayGridDefinition`, `ClayColumnDefinition`) и классы доступа к БД
(`ClayGridDefinitionData`, `DynamicSql`) живут в **`Clayzor.Lib.Entities.DynamicGrid`** — см. [../Clayzor.Lib.Entities/AGENTS.md](../Clayzor.Lib.Entities/AGENTS.md).

**Выполненные шаги (G0–G14):**
- G0 — `scripts/dynamic-grid/schema.sql`: 3 таблицы + триггер-upsert + сид #140
- G1 — опции, схема, DI, тесты TG1
- G1b — `DynamicSql` в `Clayzor.Lib.Entities.DynamicGrid`
- G2 — модели (`ClayGridDefinition`, `ClayColumnDefinition`), `ClayGridDefinitionData`, перенос `ClayGridSchemaMap` в Entities, тесты TG2
- G3 — `ClayColumnKind`, `ClayColumnTypeMap`, `ClayColumnFormat`, тесты TG3
- G4 — `ClayGrid.Dynamic.cs`: динамический рендер, загрузка определения/колонок/данных из БД
- G5 — `ClayGridLinkResolver`, кнопки действий (edit/add/delete) в динамическом режиме
- G6 — `ClayGridUserParamsData` в Entities: сохранение/чтение пользовательских параметров (INSERT-only), тесты TG5
- G7 — `GridStateSerializer`, сохранение/восстановление состояния в `ClayGrid.Dynamic.cs` (CLID, 5 параметров), тесты TG6
- G8 — `ClayGridUrlFilterParser`, разбор URL-фильтра `op~value`, интеграция в `InitDynamicMode`, тесты TG4
- G9 — URL-параметр `cols` (видимость колонок), исключение forced-параметров из сохранения
- G10 — `ClayListColumnType` (Тип 5): справочник через подзапрос, кеш, CellTemplate с резолвом text по value, тесты TG7
- G11 — `ClayIconColumnType` (Тип 9): 3-колоночный подзапрос, `<img>` с tooltip, тесты TG7
- G12 — `ClayConditionBoolColumnType` (Тип 6) и `ClayConditionListColumnType` (Тип 11): фильтр-онли, не выводятся в гриде, тесты TG7
- G13 — `ClayHtmlSanitizer`, `ClayHtmlColumnType` (Тип 8), `ClayLimitedTextColumnType` (Тип 12), Тип 4 (Ссылка): `<a>`, обрезка+tooltip, `AddMarkupContent`, тесты TG8
- G14 — `ClayDateTimeLocalColumnType` (Тип 10), `ClayTimeLocalColumnType` (Тип 13), `ClayDateTimeConverter`: UTC→локальный пояс, `_clientOffset` из JS, тесты TG8

**Выполненные багфиксы (GF1–GF2):**
- GF1 — `ClayDynamicRow`: единый тип строки динрежима (`IClayGridRow` + `IDetailRow` + `IReadOnlyDictionary`), убран `InvalidCastException` в `LoadDynamicData`
- GF2 — `InitDynamicMode()` вызывает `NotifyQueryChanged()` — первая загрузка данных при открытии страницы, без ручного «Обновить»
- GF3 — `OnParametersSet()` не сбрасывает `_pageNumber` в динамическом режиме — пагинация не прыгает на 1 при ре-рендере
- GF4 — скрытые по умолчанию колонки (`Порядок=0/NULL`) регистрируются в `_columnBySqlName` и `_hiddenSqlNames` — доступны в диалоге «Настройка колонок»
- GF5 — `ApplyColumnsState` мерджит сохранённое состояние колонок с дефолтом: новые колонки из `ЗапросыКолонки` появляются у пользователей с сохранённым состоянием
- GF6 — `BuildColumnSettingsItems()` строит список из `_columnOrder`, а не из `_columnBySqlName` — фильтр-онли колонки (Тип 6/11) не попадают в диалог настройки

**Выполненные багфиксы (GF8+):**
- GF8 — tie-breaker в сортировке колонок: `ORDER BY [Порядок], [КодКолонки]` в SQL и `.ThenBy(ColumnId/Column)` в LINQ — детерминированный порядок при одинаковом `Порядок`
- GF9 — снятый фильтр затирается в БД: `SerializeFilter(...) ?? string.Empty` — пустая строка вместо `null`, `DeserializeFilter("")` корректно возвращает `null`
- GF10 — `_dynamicError` + `MudAlert` в `ClayGrid.razor`: при отсутствии `?id=` или несуществующем запросе — предупреждение вместо пустой страницы
- GF11 — `InitClientOffset()` + `clayGridTimeZone.js`: чтение часового пояса клиента через JS, Тип 10/13 отображают локальное время
- GF12 — `SaveParamIfChanged`: запись в БД только изменившихся параметров (кеш `_dynamicSavedParams`), а не 5 `INSERT` на каждую загрузку
- GF13 — `TryGetSelectionId`: чекбоксы выбора строк в динрежиме (ID из `_dynamicDef.IdColumn` вместо `Entity.Id`)
- GF16 — `dp.Add("search", ...)` в `LoadDynamicData`: поиск в динрежиме больше не падает `SqlException`

**Выполненные шаги группировки (GG1+):**
- GG1 — `ClayDynamicGroupMapper`: маппинг словарей агрегатного GROUP BY в `GridGroupRow` (чистые функции, DBNull→null, K0 null→""). **Заменён на `ClayGroupRowMapper` в GN1**
- GG2 — `LoadDynamicGroupedData`: конвейер группировки (агрегат→дерево→layout→детали), `LoadDynamicData` → диспетчер + `LoadDynamicFlatData`
- GG3 — `_dynamicExpandedGroups` + `ToggleDynamicGroup`: раскрытие/сворачивание групп с автопереходом страницы, `HandleGroupToggle` — единый диспетчер клика
- GG4 — `GroupRowHostKey` учитывает `HasDynamicEdit`: условие выбора сервисной колонки совпадает с условием рендеринга в `ClayGrid.razor`
- GG5 — чипы лотка: `IsLevelFullyExpanded`/`ToggleLevel` — диспетчер dynamic/static, `ToggleDynamicLevelExpanded` с каскадным раскрытием родителей
- GG6 — `ResolveGroupDisplayValue`: заголовки групп Тип 5/9 показывают наименование вместо кода
- GG7 — включение группировки: `ResetDynamicExpandedGroups` при смене состава/порядка `_groupColumns`, `Groupable = true` (уже было), `ApplySavedGroups` (уже был)
- GG8 — tri-state чекбоксы групп: `LoadDynamicGroupChildIdsAsync`, диспетчер `LoadChildIdsForGroupsAsync`, расширен guard в `razor.cs`
- GG9 — группировка в диалоге настройки колонок: секция Grouping (зеркало Sorting) с MudSwitch + бейджем, попутный фикс утечки `_hiddenSqlNames`, кнопка «+» в лотке

**Выполненные шаги снятия потолка уровней (GN1+):**
- GN1 — `GridGroupRow.Keys` вместо `K0/K1/K2`, `BuildGroupAggregateSql` без потолка (N уровней), `ClayGroupRowMapper` (общий маппер, заменил `ClayDynamicGroupMapper`), перевод 5 статических `Db.QueryAsync<GridGroupRow>` на словари + маппер, временный мост в `BuildAggregates`
- GN2 — `BuildAggregates` на N уровней: `depth = Keys.Count - 1`, `NULL` — законный ключ, `FullKey` без дубликатов при 3+ уровнях, `EmptyGroupDisplay` + `ToDisplay`
- GN3 — `BuildGroupKeyWhere`: детальный `WHERE` с `IS NULL` для null-ключей, замена копипасты `dk{i}` в 4 местах, `LoadDynamicGroupChildIdsAsync` с `FullKey.Split` → `rawKeys`
- GN4 — `BuildInterleavedHeaders`: C#-interleaving экспорта на N уровней, единый метод в `ClayGroupingEngine`, замена копипасты в `Export.Excel` (основной + subtree), `Export.Selected`, `Dynamic.Export`. Согласование `FullKey`/`DisplayValue` с движком (`EmptyGroupDisplay`), нормализация `null` → `""`
- Оркестратор: `promts/GN0_README_grouping_levels.md`, промты `GN1`–`GN4`

**Выполненные шаги печати и Excel (GE1+):**
- GE1 — `IClayGridCellReader` + `ClayReflectionCellReader`: вынос чтения ячейки из генераторов в абстракцию. Старая сигнатура с `Type entityType` → обёртка над `new ClayReflectionCellReader`. Поведение статики не изменилось
- GE2 — `ClayDynamicCellReader`: динамическая реализация `IClayGridCellReader`. Тип 1/2/3/7 сырыми для Excel, Тип 5/9 через справочники, Тип 10/13 со смещением, Тип 8 StripHtml, Тип 12 полный текст. Без БД, всё через конструктор
- GE3 — `ClayGrid.Dynamic.Export.cs`: загрузка строк для экспорта (текущая страница / все / выбранные), плоско и с группировкой (C# interleaving). Агрегат через `ClayGroupRowMapper`, детальный WHERE через `BuildGroupKeyWhere`, белый список `IdColumn`
- GE4 — печать в динрежиме: `CreateDynamicCellReader`, 3 метода `BuildDynamicPrintHtml*`, диспетчер `Dynamic/static` в `Print*Internal` (ExportMenu.cs)
- GE5 — Excel в динрежиме: `DynamicExcelExportAsync` (switch по режиму, генератор + base64 + JS-скачивание), `ClayGridExportFileName.Sanitize` (вынос из `ClayGridPageBase`), диспетчер в `Excel*Internal`
- GE6 — включение: `HasBatchOperations`, меню групповых операций вынесено из `@if (SelectVisible)` в `@if (HasBatchOperations)`, откат GF15
- Оркестратор: `promts/GE0_README_dynamic_export.md`, промты `GE1`–`GE6`

**Выполненные багфиксы (GB1+):**
- GB1 — кнопка «Выбрать записи» в динамическом гриде: `SelectVisible="true"` в `Home.razor`, `SelectAvailable` (только при `IdColumn`)
- GB8 — шлюз `SemaphoreSlim` на `SqlConnection` (`RunAsync<T>`), `DynamicSql` через `RunAsync`, MARS выключен
- GB9 — `OnHeaderTriToggle`: `dr.Item is Entity` → `TryGetSelectionId(dr.Item, out var eid)`, орфан `using Clayzor.Lib.Entities`
- GB2 — `BuildDynamicExportRowsForCurrentPage`: `covered`-список вместо `continue` на `_dynamicExpandedGroups`/`GroupKeys.Count`, свёрнутые и промежуточные группы с данными, счётчики из `_dynamicGroupRoots`
- GB6 — `BuildExportRows` (статика): тот же алгоритм, что GB2 — `covered`, `BuildGroupKeyWhere`, `BuildInterleavedHeaders`, счётчики из `_groupTreeRoots`, убраны per-header агрегаты и `pk{i}`
- GB10 — печать «Текущая» = строго экран (`Items`/`_rows` без БД), печать «Все» = весь список раскрытым, `expandedGroups` убран из `ClayGridPrintHtmlGenerator`, переименованы `BuildAllRowsFor*` → `BuildAllRowsForExport`, удалены `BuildAllRowsForPrint`/`BuildAllGroupedRowsForPrint`
- GB7 — общие стили: `clay.css` в RCL (`wwwroot/css/`) вместо двух копий `app.css`, `App.razor` подключает `_content/Clayzor.Lib.Web.Controls/css/clay.css`
- GB3 — индикатор долгой операции: `RunBusyAsync` + `MudOverlay` + `.clay-grid-busy`, единый для печати и Excel, убран JS-спиннер и `.clay-print-spinner`
- GB4 — диалог настройки колонок: единая 5-колоночная grid-сетка, `ContentClass` с нулевым паддингом (sticky без щели), `--clay-cs-*` переменные, `MaxWidth.Small`
- GB5 — кнопки диалога настройки колонок: `flex-wrap`, `flex-shrink: 0` у пары Отмена/Применить, `MaxWidth.Small` (зависит от GB4)
- GB11 — сохранение порядка колонок после перетаскивания в шапке грида: `OnColumnDrop` → `async Task` + `await SaveDynamicState()` (данные не перезагружаются)
- Оркестратор: `promts/GB0_README_grid_ux_fixes.md`, промты `GB1`–`GB11`

**Стили компонентов:** общий стиль грида/треев/чипов/диалогов живёт в `wwwroot/css/clay.css`. Правится он, а не копии в приложениях (см. `STYLE_RULES.md` §0).

### Services

| Сервис | Назначение |
|---|---|
| **ClayErrorService** (Scoped) — хранит состояние последней ошибки SQL, реализует `ISqlErrorHandler`. Используется `ClayErrorBar` |
| **ISqlErrorHandler** (DALC) — интерфейс, вызываемый `DbManager` при `SqlException`. Регистрируется в DI |
| **ClayReflectionCellReader** — читает значение ячейки через рефлексию по `[Column]`-атрибутам. Реализует `IClayGridCellReader`. Используется генераторами печати и Excel в статическом режиме (GE1) |
| **ClayGridExportFileName** — `Sanitize(title)` — убирает недопустимые символы из имени файла. Общий для статики и динамики (GE5) |

### Codebehind-структура ClayGrid

После рефакторинга (задачи 04–05 мастер-плана) логика `ClayGrid<TEntity>` разнесена по partial-файлам. Все файлы объявляют `public partial class ClayGrid<TEntity> where TEntity : class` в namespace `Clayzor.Lib.Web.Controls.Components.Grid`. Базовый класс (`ComponentBase`) и реализуемые интерфейсы (`IClayGrid`, `IDisposable`, `IAsyncDisposable`) — только в основном файле.

| Файл | Строк | Содержание |
|---|---|---|
| `ClayGrid.razor` | ~640 | Разметка грида (MudDataGrid, тулбар, панели, колонки) |
| `ClayGrid.razor.cs` | ~540 | Основа: интерфейсы, параметры, поля (`_lastQuery`, `_columnById`, `_columnBySqlName`, `_columnOrder`, `_hiddenSqlNames`), инициализация, регистрация колонок, `RegisterCellTemplate`, `NotifyQueryChanged`, высота грида, `DisposeAsync`, `ColumnMenuMode`, `OpenColumnSettings`, `BuildColumnSettingsItems` (переиспользуется для печати/экспорта) |
| `ClayGrid.Search.cs` | 18 | `_searchText`, `DebounceTimer`, обработчики поиска |
| `ClayGrid.Sorting.cs` | 66 | `_sortState`, `ToggleSort`, `HandleSortClick`, `GetSortBadge` |
| `ClayGrid.Grouping.cs` | ~250 | `OnGroupToggle` (параметр-событие), `GroupRowHostKey` (авто-выбор хост-колонки для заголовка группы), `IsGroupRowHost`, `_groupColumns`, `_trayExpanded`, `AddGroupColumn`, `RemoveGroupColumn`, `OnChipDragStart/End`, `OnTrayDragOver/Drop`, `GroupColumns`, `OnGroupTriToggle`, `OnHeaderTriToggle`, `_groupChildIds` |
| `ClayGrid.Filtering.cs` | ~420 | `_filterRoot`, `HasComposite`, `ValueFilterLeaves` (V12), `_valueFilterDisabledColumns` (V8), `OpenFilterDialog` (+`initialOperator`), `OpenValueFilterDialog` (V7), `ApplyValueFilter`, `RemoveValueFilter`, `DescribeValueFilter` (V8), `BuildCurrentQuery`, `OpenCompositeFilterDialog`, чипы, фильтр-трей |
| `ClayGrid.DragDrop.cs` | 86 | `_dragSourceIndex`, drag-and-drop чипов группировки (перемещение/перестановка в трее) |
| `ClayGrid.Selection.cs` | 113 | `_selectMode`, `_selectAllChecked`, `_selectedIds`, `OnRowSelectAsync`, `SelectAllAsync`, `DeselectAllAsync`, `ToggleSelectMode`, персистентность выделения. В динрежиме кнопка выбора появляется только при заполненном `Запросы.ID` (GB1) |
| `ClayGrid.ExportMenu.cs` | ~240 | `_isExporting`, `_openSubGroups`, `ToggleSubGroup`, `ResolveExportColumnsAsync` (prompt → настройка/как на странице/null), `Print{CurrentPage,Selected,All}Internal` (через `BuildPrintHtmlForCurrentPageAsync` / `BuildPrintHtmlAsync` / `BuildPrintHtmlForSelectedAsync`), `Excel{CurrentPage,Selected,All}Internal` |
| `ClayGrid.Paging.cs` | 59 | `_pageSize`, `OnPageSizeChanged`, `PrevPage`, `NextPage`, `LastPage` |
| `ClayGrid.Dynamic.cs` | ~120 | Динамический режим: инжекты (`DbManager`, `IOptions<ClayGridDynamicOptions>`, `NavigationManager`), параметры (`Dynamic`, `DynamicGridId`), `InitDynamicMode` (загрузка определения + колонок из БД, регистрация `ClayColumnMeta`, CellTemplate из словаря, первая загрузка через `NotifyQueryChanged`), `LoadDynamicData` (WHERE из композитного фильтра через `ClayCompositeSqlBuilder.Build` + поиск через `BuildWhereClause`, оборачивание строк в `ClayDynamicRow`, выполнение через `DynamicSql`), `ResolveDynamicGridId` (из параметра или query-строки) |
| `ClayGrid.Dynamic.Export.cs` | ~200 | Загрузка строк для экспорта в динрежиме (GE3): `BuildDynamicExportWhere`, `BuildDynamicSelectAllSql`, `BuildDynamicExportRowsForCurrentPage` / `ForAll` / `ForSelected`, `BuildDynamicGroupedExportRows` (C# interleaving), `CollectDynamicGroupCounts` |

**Правила модификации:**
- Новые поля/методы добавлять в соответствующий тематический файл, а не в `ClayGrid.razor.cs`
- `ClayGrid.Filtering.cs` будет переписан задачами 10–11 (переход на дерево фильтра), поэтому изолирован
- При добавлении using — в тот файл, где используется тип
- Базовый класс и интерфейсы — только в `ClayGrid.razor.cs`

### Codebehind-структура ClayGridPageBase

После рефакторинга (задача 06 мастер-плана) логика `ClayGridPageBase<T>` разнесена по partial-файлам. Все файлы объявляют `public abstract partial class ClayGridPageBase<T> where T : Entity` в namespace `Clayzor.Lib.Web.Controls.Components.Grid`. Базовый класс (`ComponentBase`) и реализуемые интерфейсы (`IClayGridDataLoader`) — только в основном файле.

| Файл | Строк | Содержание |
|---|---|---|
| `ClayGridPageBase.cs` | ~530 | Ядро: `[Inject]`-сервисы, `Grid`, `LoadData`, `LoadFlatData`, `LoadGroupedData`, `LoadDistinctValuesAsync` (V4), `CloneFilterTreeWithoutColumn`, `CheckHasBlanksAsync`, `ToggleGroup`, `OpenAddDialog`, `IClayGridDataLoader` |
| `ClayGridPageBase.ColumnTypes.cs` | 83 | Вывод типов колонок: `_idColumnName`, `_propertyMap`, `_inferredColumnTypes`, `FilterColumnTypes`, `GetIdColumnName`, `BuildPropertyMap`, `InferFilterColumnTypes`, `MapClrTypeToColumnType` |
| `ClayGridPageBase.Export.Excel.cs` | 208 | Экспорт в Excel: `IClayGridDataLoader.ExcelExportAsync`, `BuildAllRowsForExcel`, `BuildAllGroupedRowsForExcel`, `BuildExportRows`, `CollectCounts`, `SanitizeFileName` |
| `ClayGridPageBase.Export.Print.cs` | 89 | Печать всех данных: `BuildAllRowsForPrint`, `BuildAllFlatRowsForPrint`, `BuildAllGroupedRowsForPrint` |
| `ClayGridPageBase.Export.Selected.cs` | 225 | Экспорт/печать выбранных: `BuildPrintHtmlForSelectedAsync`, `BuildAllRowsForSelected`, `BuildAllFlatRowsForSelected`, `BuildAllGroupedRowsForSelected`, `GetGroupKeysByDepth`, `CollectKeysByDepth` |

**Правила модификации:**
- Новые поля/методы добавлять в соответствующий тематический файл, а не в `ClayGridPageBase.cs`
- При добавлении using — в тот файл, где используется тип
- Базовый класс и интерфейсы — только в `ClayGridPageBase.cs`

## Server-side grouping architecture

Группировка выполняется **на стороне SQL Server** двумя отдельными запросами (подход DevExpress Blazor Grid). Реализация — `ClayGroupingEngine` (статический класс в `Components/Grid/ClayGroupingEngine.cs`).

1. **Запрос групповых агрегатов** — `GROUP BY` + `COUNT(*)`, возвращает уникальные значения группировки и количество записей
2. **Запрос детальных строк** — выборка конкретных записей с `ROW_NUMBER()` и фильтром по значениям группы

### Модель данных
- `IClayGridRow` — маркерный интерфейс строки в плоском списке (`Clayzor.Lib.Web.Controls/Components/Grid/ClayGridRow.cs`)
- `GroupHeaderRow` — заголовок группы: `FullKey`, `DisplayValue`, `ItemCount`, `Depth`, `IsExpanded`
- `DetailRow<T>` — обёртка сущности: `Item`, `GroupKey`, `Depth`
- `GroupedPage<T>` — результат запроса: `Rows` (плоский список) + `TotalEffectiveRows`
- `ClayDataQuery.ExpandedGroups` — `HashSet<string>` полных ключей развёрнутых групп (разделитель ``)

### Рендеринг
- **Плоская модель**: заголовки групп и строки детализации передаются как единый `IEnumerable<IClayGridRow>`
- `ClayGrid` сам решает, какая колонка хостит `<ClayGroupHeader>` для строк `GroupHeaderRow`
  (`GroupRowHostKey`/`IsGroupRowHost` в `ClayGrid.Grouping.cs`: колонка редактирования, если есть,
  иначе первая видимая колонка данных, не скрытая группировкой). Страница подписывается только через
  `OnGroupToggle="ToggleGroup"` на теге `<ClayGrid>` — вручную вставлять `<ClayGroupHeader>` в
  `CellTemplate` не нужно:
  ```razor
  <ClayGrid TEntity="IClayGridRow" ... OnGroupToggle="ToggleGroup">
      ...
      <ClayColumn TEntity="IClayGridRow" ColumnId="2">
          <CellTemplate>
              @if (context.Item is DetailRow<MyEntity> detail)
              {
                  <MudText Style="@($"padding-left:{(detail.Depth + 1) * 16}px")">@detail.Item.Id</MudText>
              }
          </CellTemplate>
      </ClayColumn>
  </ClayGrid>
  ```
- `ClayGroupHeader` — встроенный компонент для отображения иконки раскрытия/сворачивания и количества элементов, вызывается гридом автоматически
- `ClayColumn` автоматически получает Title (DisplayName), строит HeaderTemplate с drag&drop и серверной сортировкой, скрывает колонку при группировке
- **Запрещено** использовать MudBlazor `GroupBy`/`Groupable`/`GroupExpanded`/`GroupTemplate` — группировка управляется сервером
- `SortMode` на MudDataGrid **не задаётся** — порядок строк определяется серверным SQL

### Пагинация с группами
- Каждый заголовок группы = 1 эффективная строка, каждая строка детализации = 1
- `TotalCount` = общее эффективное количество строк (а не сырых записей)
- При сворачивании/разворачивании группы количество видимых строк меняется, страница пересчитывается
- При разворачивании последней группы на странице, если её детали не влезают — автоматический переход на следующую страницу

### Многоуровневая группировка
- SQL: `GROUP BY Col1, Col2, ...` — возвращает листовые агрегаты. Число уровней не ограничено (GN1): `SELECT` отдаёт `K0..K{n-1}` по числу колонок
- `GridGroupRow.Keys` — список значений группировочных колонок (N уровней, GN1). `null` — законное значение ключа, а не признак отсутствия уровня
- C#: синтетические родительские узлы создаются из листовых, `ItemCount` родителя = сумма дочерних
- `ComputeParentCounts()` рекурсивно вычисляет `ItemCount` для всех промежуточных уровней
- Уровень вложенности: `Depth` (0 = внешний), отступ заголовка: `Depth * 16px`. Строки детализации отступают на `(Depth + 1) * 16px` — на один уровень глубже родительской группы
- **`ItemCount` учитывается только для листовых узлов** (`Children.Count == 0`) — родительские узлы не имеют собственных строк детализации, их «строки» = дочерние группы
- При сворачивании группы, если текущая страница становится за пределами `maxPage = ceil(TotalCount / PageSize)`, происходит автоматический возврат на `maxPage`

### Отображение колонок
- Колонки, участвующие в группировке, скрываются в гриде автоматически — `ClayColumn` вычисляет `Hidden` через `IsGrouped(SqlName)` из `ClayColumnMeta`
- Иконка раскрытия/сворачивания и название группы отображаются в первой колонке (Код) через `ClayGroupHeader`

### Имена колонок в WHERE и GROUP BY
- `SearchColumns` передаются как выходные имена (например, `"НазваниеАнализа"`, `"TestTypeName"`)
- SQL-пагинация через `ROW_NUMBER()` оборачивает SELECT в подзапрос `FROM (SELECT ...) _src`, где выходные имена колонок видны напрямую — алиасы таблиц не нужны
- `GroupColumns` содержат те же выходные имена — они напрямую используются в `GROUP BY`
- `ClayGridPageBase` читает `SearchColumns`, `SelectSql`, `DefaultOrder` из `Grid` (реализация `IClayGrid`) — **abstract-свойства не нужны**, вся конфигурация передаётся через параметры `<ClayGrid>`

### Порядок сортировки в групповых запросах
- Групповой агрегатный запрос учитывает направление сортировки из `SortColumns`:
  ```sql
  ORDER BY TestTypeName DESC, Порядок ASC
  ```
- Детальные строки внутри группы сортируются по колонкам, НЕ участвующим в группировке
- **Запрещено** пересортировывать список агрегатов после получения из БД (`aggregates.OrderBy(...)`) — это уничтожает порядок, заданный `ORDER BY` в SQL. Синтетические родительские узлы строятся непосредственно внутри цикла `foreach (var gr in groupRows)` до листового узла, поэтому при обходе `aggregates` (без `.OrderBy`) порядок родитель-перед-детьми всегда соблюдается

## Server-side column filtering

Фильтрация по колонкам выполняется **на стороне SQL Server** через `ClayCompositeSqlBuilder.Build`.
Единый источник истины — дерево `ClayFilterGroupNode` (см. «Типы составного фильтра» ниже).
UI — панель фильтров (filter tray) с drag-and-drop заголовков и диалогом `ClayColumnFilterDialog` для настройки условий;
диалог составного фильтра `ClayFilterDialog` (задача 11 — панель и маршрутизация).

### Модель данных
- `ColumnType` — тип данных колонки: `Text` (10 операторов: Contains/NotContains/Equals/NotEquals/StartsWith/NotStartsWith/EndsWith/NotEndsWith/IsEmpty/IsNotEmpty), `Number` (равенство + сравнения >/</>=/<=), `Boolean` (Equals)
- `ColumnFilterOperator` — оператор сравнения: `Contains`, `NotContains`, `Equals`, `NotEquals`, `StartsWith`, `NotStartsWith`, `EndsWith`, `NotEndsWith`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `IsEmpty`, `IsNotEmpty`, `IsNull`, `IsNotNull`
- `LogicalOperator` — `And` / `Or` для объединения узлов в группе
- `ColumnFilter` — условие фильтра: `Column`, `ParamName`, `Operator`, `Value` + опциональные `LogicalOperator`, `SecondOperator`, `SecondValue`, `SecondParamName` (до двух условий на колонку). Реализует `IClayFilterNode`. Свойство `Source` (`ClayFilterSource`) — происхождение: `ColumnDialog` или `CompositeDialog`
- `ClayFilterGroupNode` — группа И/ИЛИ (`LogicalOperator Logic` + `List<IClayFilterNode> Nodes`). Реализует `IClayFilterNode`
- `ClayDataQuery.CompositeFilter` — `ClayFilterGroupNode?` — единый источник истины фильтрации. Заменил `ColumnFilters` (словарь помечен `[Obsolete]`)
- `ClayGrid._filterRoot` — приватный корень дерева фильтра в гриде. Колоночные фильтры — листья с `Source=ColumnDialog`. Составной фильтр — `Source=CompositeDialog`

### SQL-генерация
- `ClayCompositeSqlBuilder.Build(root, parameters, knownColumns, columnNameMap?)` — рекурсивно обходит дерево `ClayFilterGroupNode` и возвращает фрагмент WHERE (без слова WHERE). Безопасность: имя колонки — только из белого списка `knownColumns`; значения — только Dapper-параметры
- `ClayGridPageBase.BuildCompositeFilterClause(CompositeFilter?, dp, columnNameMap?)` — обёртка над `ClayCompositeSqlBuilder.Build`, добавляет параметры в `DynamicParameters`. Используется во всех путях загрузки (страница, группировка, экспорт, печать, выбранные)
- `ClayGridPageBase.BuildKnownColumns()` — возвращает `ISet<string>` из ключей `_inferredColumnTypes`
- `ClayDataQuery.BuildColumnFilterClause` и `BuildSingleClause` — помечены `[Obsolete]` (заменены на `ClayCompositeSqlBuilder`). `BuildSingleClause` сделан `internal`

### Типы данных фильтрации (`ColumnType`)
- `Text` — строки: Contains, NotContains, Equals, NotEquals, StartsWith, NotStartsWith, EndsWith, NotEndsWith, IsEmpty, IsNotEmpty
- `Number` — целые числа (int/long/short/byte): Equals, NotEquals, сравнения >/</>=/<=, IsNull, IsNotNull
- `Decimal` — дробные (decimal/double/float): те же что Number, редактор `MudNumericField<decimal?>`
- `Date` — даты (DateTime/DateTimeOffset/DateOnly): сравнения + IsNull/IsNotNull, редактор `MudDatePicker`
- `Boolean` — булевы: Equals, IsNull, IsNotNull
- `ClayGridPageBase.MapClrTypeToColumnType` — делегирует в `ColumnTypeRegistry.FromClr(type).Kind`
- `ClayGridPageBase.FilterLookupOptions` — необязательный virtual-словарь (SqlName → список `ClayFilterOption`) для выпадающего выбора значений в диалоге фильтра. Страница может переопределить. Грид пробрасывает в `ClayColumnFilterDialog.LookupOptions`

### Дескрипторы типов колонок (`Components/Grid/ColumnTypes/`)
- `ColumnTypeDescriptor` — абстрактный базовый класс: `Kind`, `ClrType`, `Operators`, `DefaultOperator`, `OperatorTakesValue(op)`, `Parse(string?)`, `Format(object?)`, `ToParameter(object?)`. Единая точка типозависимого поведения
- `TextColumnType`, `NumberColumnType`, `DecimalColumnType`, `BooleanColumnType`, `DateColumnType`, `ClayListColumnType` (Тип 5), `ClayIconColumnType` (Тип 9), `ClayConditionBoolColumnType` (Тип 6), `ClayConditionListColumnType` (Тип 11), `ClayHtmlColumnType` (Тип 8), `ClayLimitedTextColumnType` (Тип 12), `ClayDateTimeLocalColumnType` (Тип 10), `ClayTimeLocalColumnType` (Тип 13) — конкретные дескрипторы
- `ColumnTypeRegistry` — `FromClr(Type)` (CLR→дескриптор), `FromKind(ColumnType)` (enum→дескриптор), синглтоны
- `ClayColumnMeta.Type` — дескриптор, заполняемый при регистрации колонки; единственный источник операторов/парсинга/формата
- `ClayColumnFilterDialog` получает операторы и DefaultOperator из дескриптора, парсинг/формат — через `_descriptor.Parse/Format`

### Типы составного фильтра (`Components/Grid/Filter/`)
- `IClayFilterNode` — интерфейс узла дерева фильтра: `Clone()` (рекурсивное глубокое копирование)
- `ClayFilterGroupNode` — группа И/ИЛИ (`LogicalOperator Logic` + `List<IClayFilterNode> Nodes` + рекурсивный `Clone()`). Переиспользует существующий `LogicalOperator`
- `ColumnFilter` реализует `IClayFilterNode` — листовой узел дерева. `ColumnFilter.Source` (`ClayFilterSource`) — происхождение: `ColumnDialog` (диалог колонки) или `CompositeDialog` (настраиваемый фильтр). `IsNew` — транзиентный UI-флаг (`[JsonIgnore]`, не копируется в `Clone()`): свежедобавленное перетаскиванием условие → автофокус на «Значение»
- `ValueFilter` реализует `IClayFilterNode` — листовой узел дерева: фильтрация по набору выбранных значений (Excel-style). Поля: `Column`, `Values`, `Negate` (IN/NOT IN), `BlankChecked` (NULL/пустые строки), `ParamPrefix`. `HasValue` → `Values.Count > 0 || BlankChecked`. В одной колонке одновременно активен либо `ColumnFilter`, либо `ValueFilter` — они взаимоисключающие. Диалог настройки — `ClayColumnValueFilterDialog`, открывается через `OpenConditionRequest`
- `ClayCompositeSqlBuilder` — статический SQL-билдер. `Build(root, parameters, knownColumns, columnNameMap?)` рекурсивно обходит дерево `ClayFilterGroupNode` и возвращает фрагмент WHERE (без слова WHERE). `BuildLeaf` для `ColumnFilter`, `BuildValueLeaf` для `ValueFilter` (IN/NOT IN с учётом Negate×BlankChecked, 6 комбинаций). Безопасность: имя колонки — только из белого списка `knownColumns`; значения — только Dapper-параметры; уникальные имена параметров через сквозной счётчик (`p0, p1, …`). Листовые узлы с неизвестной колонкой отбрасываются

### Filter tray
- Панель включается кнопкой `FilterAlt` (`ShowFilterTray="true"`), скрыта по умолчанию (`_filterTrayExpanded = false`). Кнопка появляется автоматически при наличии хотя бы одного `ClayColumnDef` с `Filterable="true"`
- Иконка `FilterList` в левой части панели (`filter-tray-icon`) — кликабельный `ClayButton`, открывает `OpenCompositeFilterDialog()` → `ClayFilterDialog`
- `ToggleFilterTray()` — **не сбрасывает** фильтр при сворачивании панели. Сброс только явной кнопкой «Очистить фильтр» (`ClearAllFilters()` — обнуляет `_filterRoot` целиком)
- **Два взаимоисключающих режима** отображения чипов (свойство `HasComposite` — любой узел не-ColumnDialog):
  - **Есть составные условия** (`HasComposite == true`) → единый текстовый чип со строкой `BuildFilterDescription()` (весь фильтр одним текстом). Клик по чипу → `OpenCompositeFilterDialog()`. Крестик → `ClearAllFilters()`. Чипов колонок нет
  - **Нет составных условий** (`HasComposite == false`) → чипы по листьям `ColumnDialog` (один чип на колонку, сегменты кликабельны → `OpenFilterDialog`)
- **Перетаскивание колонки на панель** (`OnFilterTrayDrop`):
  - Нет составных условий → `OpenFilterDialog(sqlName)` (диалог колонки), лист `Source=ColumnDialog`
  - Есть составные условия → `BuildTreeWithColumnAnded(sqlName)` строит копию дерева с новым условием через `И` на верхнем уровне (если корень `ИЛИ` — оборачивает в `И(Старое, Новое)`), открывает диалог на `seedRoot`. Отмена не меняет действующий фильтр. У нового листа `IsNew=true` → автофокус на «Значение»
- Удаление колоночного фильтра: × на чипе → `RemoveFilter(sqlName)`. Колоночные условия также можно удалить из формы настраиваемого фильтра (крестик в `ClayFilterGroup`)
- Сегменты/описание строятся через `ClayFilterDescriptionBuilder`: `BuildSegments(root, getDisplayName)` → `IReadOnlyList<FilterSegment>` (для колоночных чипов); `BuildText(root, getDisplayName)` → строка для составного чипа и экспорта/печати
- **Печатная шапка** (`.clay-grid-print-descriptions`) скрыта на экране (`display:none`), видна только при печати (`@media print { display:block }`) — дублирования текста фильтра на экране нет
- Filter tray не конфликтует с grouping tray — оба могут быть открыты одновременно
- **Бейдж активных условий**: `ClayFilterDescriptionBuilder.CountActiveLeaves(root)` рекурсивно подсчитывает активные условия: `ColumnFilter` с `HasValue` (+1 если `HasSecondClause`), `ValueFilter` с `HasValue` (+1). Счётчик отображается через `MudBadge` на кнопке `FilterAlt` (скрывается при 0)

### Сериализация и URL-персистенция фильтра
- **`ClayFilterJsonConverter : JsonConverter<IClayFilterNode>`** — полиморфная JSON-сериализация дерева фильтра с дискриминатором `$type`: `"group"` → `ClayFilterGroupNode`, `"column"` → `ColumnFilter`, `"value"` → `ValueFilter`. Транзиентные поля (`ParamName`, `SecondParamName`, `ParamPrefix`, computed-свойства) помечены `[JsonIgnore]`. `object? Value` сериализуется как есть, десериализуется через `JsonElement` → ближайший CLR-тип. Атрибут `[JsonConverter]` на интерфейсе `IClayFilterNode`
- **`ClayFilterUrlHelper`** — статический хелпер: дерево → JSON → `DeflateStream` → Base64Url (и обратно). Query-параметр: `filter`
- **Восстановление при загрузке**: `ClayGridPageBase.OnAfterRenderAsync(firstRender)` читает параметр `filter` из URL, десериализует через `ClayFilterUrlHelper.Deserialize()` и вызывает `Grid.RestoreFilter(root)`. `ClayGrid.RestoreFilter()` заменяет `_filterRoot` и вызывает `NotifyQueryChanged()`

### Локализация фильтра
- **`ClayFilterStrings`** — единый источник всех строковых констант UI фильтра (заголовки, кнопки, подписи). Заменяет хардкод русских строк в `ClayFilterGroup.razor`, `ClayFilterDialog.razor`, `ClayFilterExpression.razor` и тулбаре `ClayGrid.razor`

### Интеграция на странице (через ClayGridPageBase\<T>)
Конфигурация SQL передаётся через параметры `<ClayGrid>`:
```razor
<ClayGrid TEntity="IClayGridRow"
           DataLoader="this"
           SelectSql="@SQLQueries.SELECT_МоиЗаписи"
           SearchColumns="@(new[]{"НазваниеАнализа","TestTypeName"})"
           DefaultOrder="Порядок, НазваниеАнализа"
           ... >
```
`ClayGridPageBase<T>` автоматически читает `SelectSql`, `SearchColumns`, `DefaultOrder` из `IClayGrid`, строит WHERE через `BuildWhereClause`/`BuildCompositeFilterClause` и вызывает `Entity.GetPagedAsync`/`Entity.GetCountAsync`. Во всех путях загрузки (страница, группировка, экспорт, печать, выбранные) фильтрация идёт через единый вызов `BuildCompositeFilterClause(_query.CompositeFilter, dp)`, который делегирует в `ClayCompositeSqlBuilder.Build`. В плоском и группированном режимах `SearchColumns` одни и те же — используются выходные имена колонок (видимые в подзапросе `ROW_NUMBER()`).

`FilterColumnTypes` вычисляется автоматически через рефлексию по `[Column]`-атрибутам и C#-типам свойств сущности.
Страница просто передаёт `FilterColumnTypes="@FilterColumnTypes"` в `<ClayGrid>`. Маппинг: `bool` → `Boolean`, числовые типы → `Number`, остальные → `Text`.
