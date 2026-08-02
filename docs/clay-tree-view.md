# ClayTreeView — дерево с серверной ленивой загрузкой

Компонент `ClayTreeView` — дерево с серверной ленивой загрузкой уровней. Поддерживает две модели
иерархии: **NestedSet** (вложенные множества) и **ParentKey** (ссылка на родителя).
Встроенная фильтрация с настраиваемым диалогом И/ИЛИ, пометками, сохранением состояния и одиночным
выделением.

## ClayTreeOptions — свойства экземпляра дерева

| Свойство | Тип | По умолчанию | Описание |
|---|---|---|---|
| `TreeId` | `string` | `""` | Уникальный идентификатор дерева (обязателен) |
| `SelectSql` | `string` | `""` | SQL-запрос источника данных (обязателен) |
| `HierarchyMode` | `ClayTreeHierarchyMode` | `NestedSet` | Модель иерархии |
| `Schema` | `ClayTreeSchema` | `new()` | Схема колонок (Id, Text, Parent, L, R, Level, ExtraColumns) |
| `OrderBy` | `string?` | `null` | Пользовательский ORDER BY |
| `RootId` | `object?` | `null` | Идентификатор корневого узла |
| `ConnectionStringName` | `string?` | `null` | Имя строки подключения |
| `LazyLoad` | `bool` | `true` | Ленивая загрузка уровней |
| `LevelPageSize` | `int` | `0` | Размер порции пагинации (0 — выкл) |
| `LevelPagingMode` | `ClayTreeLevelPagingMode` | `Button` | Кнопка или автоподгрузка |
| `InitialExpandLevel` | `int` | `0` | Начальный уровень раскрытия |
| `PersistExpandedState` | `bool` | `true` | Сохранять/восстанавливать состояние |
| `ShowBusyOverlay` | `bool` | `true` | Глобальный оверлей загрузки |
| `ShowLines` | `bool` | `false` | Направляющие линии иерархии |
| `IndentPx` | `int` | `20` | Отступ на уровень |
| `MaxFilterRecords` | `int` | `100` | Максимум совпадений при фильтре |
| `FilterColumns` | `IReadOnlyList<ClayTreeFilterColumn>?` | `null` | Список фильтруемых колонок |
| `FilterExcludedColumns` | `IReadOnlyList<string>` | `[]` | Колонки, исключённые из фильтра |
| `FilterDefaults` | `IReadOnlyDictionary<string, object?>` | `{}` | Значения фильтра по умолчанию |
| `SelectionMode` | `ClayTreeSelectionMode` | `Single` | Режим выделения: None / Single |

## Режимы иерархии

- **NestedSet** — вложенные множества (`[L]`, `[R]`, `[Level]`). Загрузка уровня: `[L] > @left AND [R] < @right`.
- **ParentKey** — ссылка на родителя (`[Parent]`). Загрузка уровня: `[Parent] = @parentId`. Корни: `[Parent] IS NULL`.

## Три режима загрузки

1. **Ленивый (обычный).** Загрузка корней → клик по шеврону → ленивая загрузка одного уровня.
2. **Восстановление пути.** Один путь от корня до целевой ноды (якорь или выделенная). Последовательное раскрытие уровней сверху вниз.
3. **Фильтр (пользовательский).** SQL: наложить условия → `TOP(@max+1)` совпадений → добрать предков → флаги `_ismatch` / `_hasmatchchildren`. Набор целиком, без пагинации.

## Фильтрация

Фильтр — общий `ClayFilterDialog` (дерево условий И/ИЛИ). Дерево строит список колонок через
`ClayTreeOptions.FilterColumns` (тип `ClayTreeFilterColumn`: SqlName, DisplayName, ColumnType,
опциональный UrlKey для query-параметров).

### Правила показа (1–7)

1. Верхний уровень (корни) выводится всегда.
2. Для каждой совпавшей ноды выводятся все её родители.
3. Родители, не удовлетворяющие фильтру, не считаются в счётчике.
4. Рядом с родителем скрытых совпавших потомков → «(отфильтровано)» (золотой).
5. Рядом с совпавшей нодой → «(!)» (золотой).
6. Обе надписи, если и совпала и содержит.
7. Только дефолтный фильтр → пометок и счётчика нет.
8. ~~Подсветка совпадения в тексте~~ — **отложена**: `TextColumn` — форматное поле (SQL-выражение).

### Режимы фильтра

- **Дефолтный** (из `FilterDefaults`) — тихий WHERE в ленивой загрузке, без пометок и счётчика.
- **Пользовательский** (из диалога или forced query-параметра) — полный режим с предками, пометками и счётчиком.

### Query-параметры

Формат — как у грида: `UrlKey=op~value` (forced) или `_UrlKey=op~value` (default).
`UrlKey` задаётся на `ClayTreeFilterColumn`. Приоритет: forced URL > сохранённое > default URL > FilterDefaults.

## Состояние

Два ключа, сохраняемых в таблицу пользовательских параметров (`vwНастройки`) по CLID:

| Параметр | Содержимое |
|---|---|
| `{StateParamPrefix}{hash}` | `LastExpandedId` — строковой ключ якоря |
| `{StateParamPrefix}{hash}_s` | `SelectedIds` — JSON-массив выделенных нод |

Восстановление — **один путь** (не набор веток). Ведущий ориентир — выделенная нода;
если нет — якорь.

## Выделение

- `SelectionMode.Single` — клик по ноде выделяет её (класс `clay-tree-node--selected`, фон `--mud-palette-action-selected`).
- `SelectionMode.None` — клик не выделяет.
- `SelectionMode.Multiple` — задел, не реализован (`SelectedIds` уже набор).

## `ClayTreeDynamicSettings` — секция конфигурации

```json
"ClayTree": {
  "Dynamic": {
    "FilterParamPrefix": "TreeFilter_",
    "StateParamPrefix": "TreeState_"
  }
}
```

## Пример разметки

```razor
<ClayTreeView Options="_treeOptions" OnNodeClick="OnNodeClick" @ref="_treeRef" />

@code {
    private ClayTreeOptions _treeOptions = new()
    {
        TreeId = "my-tree",
        SelectSql = "SELECT КодРесурса, РесурсРус, РесурсЛат, Parent, L, R FROM Ресурсы",
        HierarchyMode = ClayTreeHierarchyMode.NestedSet,
        Schema = new() { IdColumn = "КодРесурса", TextColumn = "РесурсРус", ExtraColumns = ["РесурсЛат"] },
        FilterColumns = new ClayTreeFilterColumn[]
        {
            new() { SqlName = "РесурсРус", DisplayName = "Ресурс (рус)", ColumnType = ColumnType.Text, UrlKey = "res_ru" },
        },
    };
}
```
