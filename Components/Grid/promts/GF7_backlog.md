# GF7 — бэклог динамического режима

Находки, обнаруженные при разборе багов «нет данных» и «не применяется настройка колонок».
**Часть уже разложена в промты GF8–GF15** — по ним и работай, а не по этому файлу. Здесь
остаётся то, что промтом не закрывается: фичи, требующие своего плана и продуктовых решений.

## Разложено в промты

| Находка | Промт | Что делает |
|---|---|---|
| `Порядок` не уникален → недетерминированный порядок колонок | `GF8_column_order_tiebreak.md` | tie-breaker по `КодКолонки` в SQL и в LINQ |
| Снятый фильтр не затирается в БД (`SerializeFilter` → `null` → запись пропускается) | `GF9_filter_clear_persist.md` | писать пустую строку |
| «Грид не найден» / нет `?id=` → пустая страница молча | `GF10_grid_not_found_message.md` | `MudAlert` вместо содержимого грида |
| `_clientOffset` всегда `TimeSpan.Zero` → Тип 10/13 в UTC | `GF11_client_timezone.md` | JS-модуль + чтение в `OnAfterRenderAsync(firstRender)` |
| 5 `INSERT` после каждой загрузки данных | `GF12_save_only_changed.md` | писать только изменившееся, кеш `_dynamicSavedParams` |
| Чекбоксы строк мертвы (`dr.Item as Entity` → `null`) | `GF13_dynamic_row_selection.md` | `TryGetSelectionId` через `_dynamicDef.IdColumn` |
| Группировка: UI есть, `LoadDynamicData` `GroupColumns` игнорирует | `GF14_disable_dynamic_grouping.md` | `Groupable = false`, UI убирается сам |
| Печать/Excel: все точки входа `if (DataLoader is null) return;` | `GF15_disable_dynamic_export.md` | не показывать подменю без `DataLoader` |
| `dp.Add("search", ...)` отсутствует в `LoadDynamicData` → поиск падает `SqlException` | `GF16_search_parameter.md` | добавить параметр, как в `LoadFlatData` |

## Остаётся в бэклоге (нужен свой план, не багфикс)

### 1. Группировка динамического грида — РАСПЛАНИРОВАНА

GF14 убирает нерабочий UI. Реализация разложена в отдельный план: **`GG0_README_dynamic_grouping.md`**
и промты `GG1`–`GG8`. GG7 частично откатывает GF14 (возвращает `Groupable = true` и
восстановление `grp+gridId`).

Ключевая находка при планировании: `ClayGroupingEngine` переиспользуется ЦЕЛИКОМ — он не зависит
от Blazor, `DbManager` и `Entity`. Динамический режим отличается от `ClayGridPageBase.LoadGroupedData`
только источником строк (`DynamicSql` вместо `Entity.GetPagedAsync<T>`) и типом обёртки
(`ClayDynamicRow` вместо `DetailRow<T>`).

### 1а. `ClayGroupingEngine`: третий уровень группировки не работает

Отдельный дефект, найденный при планировании GG. Касается ОБОИХ режимов.

`BuildGroupAggregateSql` генерирует `K0`, `K1`, `K2` и в xml-doc заявляет «до 3-х уровней»:

```csharp
for (int i = 0; i < 3; i++)
    selectParts.Add(i < groupExprs.Count ? $"{groupExprs[i]} AS K{i}" : $"CAST(NULL AS SQL_VARIANT) AS K{i}");
```

а `BuildAggregates` читает только `gr.K0` и `gr.K1`:

```csharp
if (gr.K0 is not null) keys.Add(gr.K0.ToString()!);
if (gr.K1 is not null) keys.Add(gr.K1.ToString()!);
```

`K2` игнорируется молча. Перетащив в лоток третью колонку, пользователь получает то же дерево,
что и на двух — без ошибки и без предупреждения. Чинить либо в `BuildAggregates` (третий
уровень), либо ограничением лотка двумя чипами. Требует продуктового решения: нужен ли третий
уровень вообще.

### 2. Печать и выгрузка в Excel динамического грида

GF15 убирает нерабочий UI. Реализация — `IClayGridDataLoader` поверх `DynamicSql`
(`ClayGridDynamicDataLoader`) либо развилка внутри `ClayGrid.ExportMenu.cs`. Поверхность
интерфейса большая (см. `ClayGridPageBase.cs`, строки 19–110): `BuildPrintHtmlAsync`,
`BuildPrintHtmlForCurrentPageAsync`, `BuildPrintHtmlForSelectedAsync`, `ExcelExportAsync`,
`LoadDistinctValuesAsync`, `LoadGroupChildIdsAsync`, `IsLevelFullyExpanded`,
`ToggleLevelExpandedAsync`. Три метода из восьми упираются в группировку → зависит от пункта 1.

Генераторы (`ClayGridExcelGenerator`, `ClayGridPrintHtmlGenerator`) работают с
`IReadOnlyList<ClayColumnMeta>` и строками — вероятно, переиспользуются как есть. Проверить,
как они достают значение ячейки: если через рефлексию по `Entity`, для `ClayDynamicRow`
понадобится путь через словарь.

### 3. Фильтр по значению (Excel-style) в динамическом режиме

Сейчас недоступен и не падает: `InitDynamicMode` не выставляет `AllowValueFilter` в
`ClayColumnMeta` (по умолчанию `false`) → `IsValueFilterAvailable` возвращает `false` →
значок в заголовке не рисуется.

**Мина**: `OpenValueFilterDialog` делает `DataLoader!.LoadDistinctValuesAsync(...)` с `!`.
Стоит кому-то выставить `AllowValueFilter = true` для динамических колонок — получит
`NullReferenceException`. Минимум: заменить `DataLoader!` на явную проверку. Полноценно:
`LoadDistinctValuesAsync` поверх `DynamicSql` (`SELECT DISTINCT` по колонке с учётом фильтров
других колонок) — зависит от пункта 2.

### 4. `Запросы.Пиктограмма` (`IconUrl`) не используется

G4 требовал: «Заголовок грида = `def.Title`; рядом иконка из `def.IconUrl` (если задана)».
`IconUrl` читается в `ClayGridDefinition`, но в разметку не попадает — в `ClayGrid.razor`
рядом с `@Title` только `MudText`. Мелочь, но требование G4 не выполнено.

### 5. `Запросы.IDName` (`IdNameColumn`) не используется

Спецификация: «Имя колонки названия (для карандаша)». Сейчас `HandleDynamicEdit` строит URL
только из `IdColumn`. Выяснить у заказчика, что должно происходить с `IDName`: подпись в
тултипе карандаша, заголовок формы редактирования, что-то ещё.

### 6. Модель доверия к SQL из справочника не зафиксирована

`DynamicSql.QueryPagedRowsAsync` оборачивает `Запросы.SQL` в `SELECT * FROM ({selectSql}) _q`
как есть. Значения из справочника — доверенные (заполняет администратор), пользовательский
ввод идёт через `DynamicParameters`, инъекции нет. Но `ЗапросыКолонки.Формат` для Тип 5/9 —
это тоже SQL из справочника, выполняемый через `QueryPairsAsync`/`QueryTriplesAsync`.
Зафиксировать это в `AGENTS.md` как явную модель доверия, чтобы не всплыло на ревью
безопасности как «а мы не знали».
