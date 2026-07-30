# ClayFilter — общий компонент настраиваемого фильтра

> Глобальные правила — в `/AGENTS.md`. Контекст библиотеки — в [`../../AGENTS.md`](../../AGENTS.md).

Пакет `Components/Filter/` — настраиваемый (составной) фильтр: дерево условий И/ИЛИ,
SQL-генерация, сериализация. Извлечён из грида (серия CF) для переиспользования деревом
(серия TF). **Не зависит от `ClayGrid` и `ClayColumnMeta`** — контракт колонки:
`ClayFilterColumnInfo`.

## Карта

| Что | Где |
|---|---|
| Оркестратор серии CF | [promts/_done/CF0_README_extract_filter.md](promts/_done/CF0_README_extract_filter.md) |
| Инвентаризация (CF_A) | [promts/_done/CF_A_inventory.md](promts/_done/CF_A_inventory.md) |
| Тип `ClayFilterColumnInfo` (CF_B) | [promts/_done/CF_B_filter_column_type.md](promts/_done/CF_B_filter_column_type.md) |
| Перенос и смена неймспейса (CF_C) | [promts/_done/CF_C_move_namespace.md](promts/_done/CF_C_move_namespace.md) |
| Документация (CF_D) | [promts/_done/CF_D_docs.md](promts/_done/CF_D_docs.md) |

## Состав

| Тип | Назначение |
|---|---|
| `IClayFilterNode` | Интерфейс узла дерева фильтра: `Clone()` |
| `ClayFilterGroupNode` | Группа И/ИЛИ (`LogicalOperator` + `List<IClayFilterNode>`) |
| `ColumnFilter` | Листовое условие по колонке (≤2 клаузы) |
| `ValueFilter` | Фильтр по набору значений (Excel-style, IN/NOT IN) |
| `ClayFilterSource` | Источник условия: `ColumnDialog`, `CompositeDialog`, `ValueFilter` |
| `ClayFilterColumnInfo` | **Контракт** фильтруемого поля (`SqlName`, `DisplayName`, `Type`, `Options`, `BoolLabels`) — не фильтр |
| `ClayFilterOption` | Вариант для выпадающего списка |
| `ClayFilterDialog` | Диалог настраиваемого фильтра (общий) |
| `ClayFilterExpression` | Редактор одного листового условия |
| `ClayFilterGroup` | Рекурсивный компонент узла-группы |
| `ClayFilterValueEditor` | Редактор значения по типу колонки |
| `ClayCompositeSqlBuilder` | SQL WHERE из дерева фильтра |
| `ClayFilterDescriptionBuilder` | Текст/сегменты описания фильтра |
| `ClayFilterJsonConverter` | JSON-сериализация дерева с `$type` |
| `ClayFilterUrlHelper` | Дерево → DeflateStream → Base64Url |
| `ClayFilterStrings` | Строковые константы UI фильтра |
| `ClayFilterOperatorLabels` | Русские метки операторов |

## Ключевые правила

- **Контракт колонки:** диалоги принимают `IReadOnlyList<ClayFilterColumnInfo>`, не `ClayColumnMeta`.
- **Не зависит от грида:** `grep ClayGrid\|ClayColumnMeta Components/Filter/` → пусто.
- **Грид — клиент:** маппит `ClayColumnMeta` → `ClayFilterColumnInfo` в `ClayGrid.Filtering.cs`.
- **Диалоги одной колонки** (`ClayColumnFilterDialog`, `ClayColumnValueFilterDialog`) — **гридовые**, остались в `Components/Grid/`. Ссылаются на общий `Components/Filter/` для типов и редактора.
- **Столбцы типов** (`ColumnTypeDescriptor`) — пока в `Components/Grid/ColumnTypes/`, фильтр ссылается на них.
- **SQL Server 2008 R2** — никакого `OFFSET/FETCH`.
