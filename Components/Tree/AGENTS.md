# ClayTreeView — компонент дерева

> Глобальные правила — в `/AGENTS.md`. Контекст библиотеки — в [`../AGENTS.md`](../AGENTS.md).

Пакет `Components/Tree/` — дерево с серверной ленивой загрузкой уровней, две модели иерархии
(NestedSet / ParentKey).

**План серии CT:** [promts/_done/CT/CT1_tree_view_skeleton.md](promts/_done/CT/CT1_tree_view_skeleton.md).
**Заплатки CTF:** [promts/_done/CTF/CTF0_README_fixes.md](promts/_done/CTF/CTF0_README_fixes.md).
**Кейсет-пагинация CTP:** [promts/_done/CTP/CTP0_README_level_paging.md](promts/_done/CTP/CTP0_README_level_paging.md).
**Фильтр и выделение TF (в процессе):** [promts/TF0_README_tree_filter.md](promts/TF0_README_tree_filter.md).

## Аксиома имён колонок по умолчанию

`LeftColumn = "L"`, `RightColumn = "R"`, `ParentColumn = "Parent"`.
`LevelColumn` — может отсутствовать в источнике данных и является необязательной.

## Пагинация уровня (CTP)

- **Кейсет по `L`**, только в режиме **NestedSet** (ParentKey и корневой уровень не пагинируются).
- Опции: `LevelPageSize` (0 — пагинация выключена, уровень целиком), `LevelPagingMode` (`Button` / `Scroll`).
- Курсор — одно значение `L` последнего загруженного ребёнка (`LastChildCursor`). Составной курсор не нужен (L уникален по построению NestedSet).
- SQL: `SELECT TOP (@pageSize + 1) ... WHERE [L] BETWEEN @left AND @right AND [L] > @cursor ORDER BY [L]`. Первая порция — без `@cursor`. Запрещены `OFFSET/FETCH`, `ROW_NUMBER` (SQL Server 2008 R2).
- Догрузка **дописывает** в `Children` (`LoadMoreChildrenAsync`), не заменяет. Признак завершения: `LoadedAllChildren`.
- **Условие валидности курсора:** пока порядок отображения = порядку `L`. Будущая алфавитная сортировка перестраивает `L` физически → курсор остаётся `L`. Логическая сортировка без перестройки `L` потребует пересмотра курсора.
- Scroll использует `IntersectionObserver` (JS `clayTreePaging.js`), ссылки чистятся в `Dispose`.

> **Стык с фильтром (TF).** Пагинация уровня живёт в ленивой загрузке
> (`EnsureChildrenLoadedAsync` / `LoadMoreChildrenAsync`). Фильтр-режим строит набор целиком в
> пределах `MaxFilterRecords` и пагинацию уровня не использует. Единственный стык — ленивый
> разворот отфильтрованной ноды (TF_F): он идёт обычным ленивым путём и потому автоматически
> пагинируется. Специального кода не требует; при реализации TF_F — сослаться на это.

## Карта

| Что | Где |
|---|---|
| План серии CT (CT1) | [promts/_done/CT/CT1_tree_view_skeleton.md](promts/_done/CT/CT1_tree_view_skeleton.md) |
| Заплатки CTF (CTF0–CTF6) | [promts/_done/CTF/CTF0_README_fixes.md](promts/_done/CTF/CTF0_README_fixes.md) |
| Кейсет-пагинация CTP (CTP0–CTP2) | [promts/_done/CTP/CTP0_README_level_paging.md](promts/_done/CTP/CTP0_README_level_paging.md) |
| Будущие промты (CTP3, TF, …) | [promts/](promts/) |
| Оркестратор TF | [promts/TF0_README_tree_filter.md](promts/TF0_README_tree_filter.md) |
| TF_A — разведка | [promts/TF_A_discovery.md](promts/TF_A_discovery.md) |
| TF_B — настройки | [promts/TF_B_settings_and_options.md](promts/TF_B_settings_and_options.md) |
| TF_C — колонки фильтра | [promts/TF_C_filter_columns_from_sql.md](promts/TF_C_filter_columns_from_sql.md) |
| TF_D — панель и диалог | [promts/TF_D_panel_and_dialog.md](promts/TF_D_panel_and_dialog.md) |
| TF_E — SQL и загрузка | [promts/TF_E_filter_sql_and_load.md](promts/TF_E_filter_sql_and_load.md) |
| TF_F — пометки | [promts/TF_F_marks.md](promts/TF_F_marks.md) |
| TF_G — дефолты и query | [promts/TF_G_defaults_and_query.md](promts/TF_G_defaults_and_query.md) |
| TF_H — тесты и документация | [promts/TF_H_tests_docs.md](promts/TF_H_tests_docs.md) |
| TF_I — состояние и выделение | [promts/TF_I_state_and_selection.md](promts/TF_I_state_and_selection.md) |

## Выполненные заплатки (CTF1–CTF6)

- CTF1 — NestedSet без `LevelColumn`: `NOT EXISTS`-предикат для прямых детей (блокер, без него дерево не работает глубже первого уровня). `ClayTreeSqlBuilder.BuildNestedSetSql`.
- CTF2 — уровень узла от родителя: `Level = row.Level ?? ((parent?.Level + 1) ?? 0)` в `ClaySqlTreeDataSource.MapRow` (без колонки все узлы рисовались плоскими).
- CTF3 — сброс `_expanded` в `LoadRootsAsync`: `_expanded.Clear()` вместе с `_roots`/`_byId` (кнопка «Обновить» рассинхронизировала счётчик раскрытых).
- CTF4 — устойчивость ключей: `ToKey` нормализует `DBNull` → `""`, `ToggleAsync` берёт ключ из `node.Id` вместо пересчёта. 4 теста. Оркестратор: `promts/_done/CTF/CTF0_README_fixes.md`.
- CTF5 — per-node индикатор загрузки: установка `IsLoading` в `EnsureChildrenLoadedAsync`, трёхветочный рендер в `ClayTreeNodeView.razor` (спиннер на месте шеврона), опциональный `ShowBusyOverlay` (выключен на `/tree-test`).
- CTF6 — направляющие линии иерархии: `ShowLines` в `ClayTreeOptions`, двухрежимный рендер (плоский/вложенный), CSS-псевдоэлементы для вертикалей/усов, маска обрыва у последнего ребёнка. Попутно: `ConnectionStringName` для подключения к другой БД. Оркестратор: `promts/_done/CTF/CTF0_README_fixes.md`.

## Выполненные шаги кейсет-пагинации (CTP1–CTP2)

- CTP1 — модель и SQL: `LoadedAllChildren`/`LastChildCursor` в `ClayTreeNode`, `HasMore`/`NextCursor` в `ClayTreeLoadResult`, `PageSize`/`Cursor` в `ClayTreeSource`, `TOP (@pageSize+1)` + `AND [L] > @cursor` в `BuildNestedSetSql`, `LoadMoreChildrenAsync`, `ResolveDataSourceForNode`.
- CTP2 — UI догрузки: кнопка «Загрузить ещё N» (Button) и автоподгрузка при скролле (Scroll, `IntersectionObserver`). JS `clayTreePaging.js`, `ClayTreeNodeView` — хвост уровня, `IDisposable` для снятия наблюдения. Оркестратор: `promts/_done/CTP/CTP0_README_level_paging.md`.

## Выполненные шаги фильтра (TF_B–TF_D)

- TF_B — `ClayTreeDynamicSettings` (уровень приложения, DI, секция `ClayTree:Dynamic`), `ClayTreeSelectionMode` (None|Single), расширение `ClayTreeOptions` (MaxFilterRecords, FilterExcludedColumns, FilterDefaults, FilterQueryParamMap, SelectionMode), регистрация в `AddClayTree(IConfiguration)`, `ValidateClayTreeDynamicSettings`, тесты.
- TF_C — `ClayTreeFilterColumn` (SqlName, DisplayName, ColumnType, Options, BoolLabels), `ClayTreeFilterColumnBuilder.Build()` (маппинг → `ClayFilterColumnInfo`, исключения, дедупликация), `ClayTreeOptions.FilterColumns`, тесты.
- TF_D — `ClayTreeView.Filter.cs` (partial: `_filterRoot`, `BuildFilterColumns`, `OpenTreeFilterDialogAsync`, `ClearTreeFilterAsync`, `ApplyFilterAsync`), панель `.clay-tree-toolbar` с кнопкой `FilterList` + бейдж + tooltip + кнопка удаления, заготовка счётчика. `ApplyFilterAsync` пока заглушка (TF_E).
- TF_E — SQL режима фильтра: `BuildFilterSql` (NestedSet — диапазон, ParentKey — рекурсивный CTE), `LoadFilteredAsync` в `ClayTreeData`/`ClaySqlTreeDataSource`, замена `ApplyFilterAsync` (сборка whereClause → запрос → построение дерева из плоского списка), счётчик совпадений на панели, ловушка пустого результата (→ корни), `ORDER BY` в Matches. Флаги `IsMatch`/`HasMatchChildren` в `ClayTreeRow` и `ClayTreeNode`. 4 теста `BuildFilterSql`.
