# GG — группировка динамического грида (оркестратор)

План реализации фичи «группировка» для динамического режима `ClayGrid`. Правила работы — те же,
что в `_readme_grid_dynamic.md` (разделы «Как агент работает с КАЖДЫМ промтом» и «Общие правила»).
**Один файл = один заход = один коммит. Не забегай вперёд. Не делай два файла за раз.**

## Предусловия — без них не начинать

| Промт | Почему обязателен |
|---|---|
| `GF1_dynamic_row_type.md` | `ClayDynamicRow` — тип строки детализации. Без него `Items` не собрать |
| `GF2_initial_load.md` | первая загрузка данных |
| `GF4_default_hidden_columns.md` | регистрация колонок и `_hiddenSqlNames` |
| `GF5_columns_state_merge.md` | мердж состояния колонок |
| `GF16_search_parameter.md` | `dp.Add("search", ...)` — иначе группировка с активным поиском упадёт `SqlException` |
| `GF14_disable_dynamic_grouping.md` | **выполнен и будет частично откачен в GG7.** Если GF14 не выполнялся — ничего страшного, GG7 просто приведёт код к нужному виду |

Если хоть один не выполнен — ОСТАНОВИСЬ и скажи об этом. Не пытайся сделать его «заодно».

## Что уже есть и переиспользуется как есть

**`Components/Grid/ClayGroupingEngine.cs` — читать целиком перед GG2.** Это статический движок
группировки: строит SQL, превращает плоский `GROUP BY` в дерево, считает страничную разметку.
Он **не зависит** от Blazor, MudBlazor, `DbManager` и `Entity` — работает со строками и
`GridGroupRow`. Динамический режим переиспользует его ПОЛНОСТЬЮ:

- `BuildGroupAggregateSql(selectSql, groupExprs, where, sortColumns)` — агрегатный `GROUP BY`;
- `BuildAggregates(groupRows)` → `BuildTree(...)` → `ComputeParentCounts(...)` — дерево групп;
- `ComputeEffectiveRows(root, expanded)` — сколько строк занимает узел на странице;
- `WalkTree(...)` — разметка страницы в `List<GridLayoutItem>`;
- `BuildDetailPageSql(selectSql, where, detailOrder)` — постраничные детали внутри группы;
- `BuildDetailOrder(fullOrderBy, groupColumns, fallbackOrder)` — `ORDER BY` внутри группы.

Типы `GridGroupRow`, `GridGroupAgg`, `GridGroupNode`, `GridLayoutItem`, `GroupHeaderRow` — тоже
готовы. **Ничего из этого не переписывай и не копируй.** Эталон использования —
`ClayGridPageBase.LoadGroupedData` (`ClayGridPageBase.cs`, конец файла): читай его как образец,
динамический вариант отличается только двумя вещами — откуда берутся строки (`DynamicSql`
вместо `Entity.GetPagedAsync<T>`) и во что они заворачиваются (`ClayDynamicRow` вместо
`DetailRow<T>`).

## Чего нет и что придётся сделать

| Пробел | Промт |
|---|---|
| `LoadDynamicData` игнорирует `query.GroupEnabled` / `GroupColumns` — всегда плоский список | GG2 |
| `GridGroupRow` мапится Dapper'ом (`Db.QueryAsync<GridGroupRow>`), а `Controls` не имеет права звать Dapper напрямую | GG1 |
| `ExpandedGroups` живёт в `_query` СТРАНИЦЫ (`ClayGridPageBase`). В динамическом режиме страницы нет | GG3 |
| `OnGroupToggle` — `EventCallback` [Parameter], который в статике подписывает страница (`OnGroupToggle="ToggleGroup"`). `Home.razor` его не подписывает — клик по шеврону не делает ничего | GG3 |
| `GroupRowHostKey` возвращает `"__edit__"` только при `EditDialogType is not null`, про `HasDynamicEdit` не знает | GG4 |
| Чипы уровней (развернуть/свернуть всё) — под `@if (DataLoader is not null)`, а `IsLevelFullyExpanded`/`ToggleLevelExpandedAsync` живут в `IClayGridDataLoader` | GG5 |
| Колонки Тип 5 (Список) и Тип 9 (Пиктограмма) в заголовке группы покажут код, а не наименование | GG6 |
| `Groupable = false` (GF14), `ApplySavedGroups` не вызывается | GG7 |
| Tri-state чекбоксы групп (`LoadGroupChildIdsAsync`) | GG8 |

## Порядок выполнения

| Файл | Что делает | Фича видна пользователю? |
|------|-----------|--------------------------|
| `GG1_group_rows_query.md` | `DynamicSql.QueryGroupRowsAsync` + маппер словаря в `GridGroupRow` | нет |
| `GG2_load_grouped_data.md` | `LoadDynamicGroupedData` — весь конвейер: агрегат → дерево → layout → детали | нет |
| `GG3_expanded_state.md` | `_dynamicExpandedGroups`, `HandleGroupToggle`, автопереход страницы | нет |
| `GG4_group_row_host.md` | `GroupRowHostKey` учитывает `HasDynamicEdit` | нет |
| `GG5_level_toggles.md` | чипы «развернуть/свернуть всё» без `DataLoader` | нет |
| `GG6_group_display_lookup.md` | заголовок группы для Тип 5/9 — наименование, а не код | нет |
| `GG7_enable_grouping.md` | **включение**: `Groupable = true`, восстановление `grp+gridId` | **да** |
| `GG8_group_checkboxes.md` | tri-state чекбоксы групп (требует `GF13`) | да |
| `GG9_grouping_in_column_settings.md` | секция группировки в диалоге «Настройка колонок» + кнопка «+» в лотке: группировка по колонкам, которых нет в выводе | да |

**Фича включается только в GG7.** До него всё, что ты пишешь, лежит мёртвым кодом: кнопки
«Группировать» в динамическом гриде нет, `Groupable = false`. Это сделано намеренно — так каждый
шаг компилируется и не ломает работающий грид. Не пытайся «включить пораньше, чтобы проверить»:
в каждом промте есть свой блок «Проверка», выполнимый на этом шаге.

`GG8` можно не делать: без него грид группируется и листается, нет только чекбоксов у заголовков
групп. Требует `GF13_dynamic_row_selection.md`.

`GG9` закрывает упущение GG7: скрытые колонки (`Порядок = 0`) группируемы по данным и движку,
но в UI до них не добраться — чип попадает в лоток только от заголовка колонки (drag или меню ⋮),
а у скрытой колонки заголовка нет. Проверка GG7 «перетащить заголовок в лоток» для них
невыполнима. Новый диалог для этого не нужен: `ClayColumnSettingsDialog` после GF6 уже
показывает скрытые колонки, ему не хватает только переключателя группировки — GG9 делает его
зеркалом существующей секции сортировки (`ShowSorting` → `ShowGrouping`, `SortPriority` →
`GroupPriority`). Попутно GG9 чинит утечку: сгруппированная колонка уезжала в `_hiddenSqlNames`
и не возвращалась после снятия группировки.

Фильтр-онли колонки (Тип 6/11) GG9 НЕ добавляет и добавить не может: у них нет
колонки-источника, `GROUP BY` строить не по чему.

## Ограничение уровней снято отдельным планом

Раньше здесь стояло: «группировка поддерживает 2 уровня, а не 3, не трогай `BuildAggregates`».
Разбор показал, что дело хуже: три уровня не игнорировались, а **дублировали заголовки групп и
показывали пересекающуюся детализацию**, а `NULL` в группировочной колонке ронял грид
`InvalidOperationException`. Всё это лечит план **`GN0_README_grouping_levels.md`** (`GN1`–`GN4`):
уровней столько, сколько чипов в лотке, `NULL` — законное значение ключа.

GN и GG независимы и могут выполняться в любом порядке; там, где они пересекаются
(`ClayDynamicGroupMapper` из GG1, детальный `WHERE` из GG2, `LoadDynamicGroupChildIdsAsync`
из GG8), правки прописаны в промтах GN с пометкой «если файла нет — пропусти».

Формат `grp+gridId` (`col1,col2` через запятую, см. `GridStateSerializer.SerializeGroups`)
не меняй — он уже в БД.

## Модель доверия

`_groupColumns` наполняется из двух источников: перетаскивание колонки в лоток (безопасно —
SqlName из `_columnBySqlName`) и восстановление из `vwНастройки` (`ApplySavedGroups`). Второй
источник — строка из БД. `ApplySavedGroups` уже фильтрует по `_columnBySqlName.ContainsKey(sqlName)` —
**этот фильтр обязателен и в GG7**: имена группировочных колонок подставляются в
`GROUP BY {expr}` как есть, без параметризации. Убери проверку — получишь SQL-инъекцию через
таблицу настроек.
