> Часть серии **CTP**. Прочитать `CTP0_README_level_paging.md`. Делать ТОЛЬКО этот шаг.
> **Высокий риск** — трогает билдер, загрузку и модель. Перед началом git-коммит.

# CTP1 — модель узла, кейсет в SQL, опции пагинации

Ядро серии: научить ленивый NestedSet-уровень грузиться порциями по кейсету `L`. UI (кнопка/
скролл) — в CTP2; здесь загрузка работает, но триггерится программно (для теста — вручную).

## Прочитать

- `Clayzor.Lib.Entities/Tree/ClayTreeSqlBuilder.cs` — `BuildNestedSetSql` (ветка прямых детей),
  константы параметров, `BuildOrderBy`;
- `Clayzor.Lib.Entities/Tree/ClayTreeData.cs` — `BuildParams`, `LoadLevelAsync`, маппинг;
- `Components/Tree/DataSources/ClaySqlTreeDataSource.cs` — `LoadLevelAsync`, `MapRow`;
- `Components/Tree/ClayTreeView.Loading.cs` — `EnsureChildrenLoadedAsync`;
- `Components/Tree/Models/ClayTreeNode.cs`, `ClayTreeOptions.cs`, `ClayTreeLoadRequest.cs`.

## 1. Опции

`ClayTreeOptions`, блок «Загрузка»:

```csharp
/// <summary>
/// Размер порции при постраничной загрузке уровня. 0 — пагинация выключена (уровень целиком).
/// Действует ТОЛЬКО в режиме NestedSet; в ParentKey игнорируется (нет ключа L для кейсета).
/// </summary>
public int LevelPageSize { get; set; } = 0;

/// <summary>Способ запроса следующей порции уровня: кнопкой или автоподгрузкой при скролле.</summary>
public ClayTreeLevelPagingMode LevelPagingMode { get; set; } = ClayTreeLevelPagingMode.Button;
```

Новый enum (файл `Components/Tree/ClayTreeLevelPagingMode.cs`):

```csharp
/// <summary>Способ подгрузки следующей порции детей уровня.</summary>
public enum ClayTreeLevelPagingMode
{
    /// <summary>Кнопка «Загрузить ещё» в конце уровня.</summary>
    Button = 0,
    /// <summary>Автоподгрузка при доскролле до конца загруженной порции.</summary>
    Scroll = 1,
}
```

Защёлка дефолтов в `ClayTreeOptionsTests`: `LevelPageSize == 0`,
`LevelPagingMode == ClayTreeLevelPagingMode.Button`.

## 2. Модель узла

`ClayTreeNode` — два поля:

```csharp
/// <summary>Все прямые дети уровня загружены (пагинация дочитана или не применяется).</summary>
public bool LoadedAllChildren { get; set; } = true;

/// <summary>
/// Курсор пагинации — значение L последнего загруженного ребёнка.
/// Следующая порция берётся как дети с L строго больше этого значения. null — порций ещё не было.
/// </summary>
public long? LastChildCursor { get; set; }
```

`LoadedAllChildren = true` по умолчанию — узел без пагинации (ParentKey, или `LevelPageSize=0`,
или лист) считается «дочитанным», UI догрузки к нему не рисуется.

## 3. Кейсет в билдере

`ClayTreeSqlBuilder.BuildNestedSetSql`, **не-корневая** ветка (дети узла). Сейчас там (после
CTF1) диапазон `[L] > @left AND [R] < @right` + либо `[Level] = @level+1`, либо `NOT EXISTS`.
Добавить **опциональный** кейсет-предикат и `TOP`:

- новый параметр билдера/источника: размер порции и курсор. Проще всего протащить их через
  `ClayTreeSource` (immutable-record) как `int? PageSize` и `long? Cursor`, заполняемые
  компонентом при пагинированной загрузке; при `PageSize is null` билдер работает как сейчас
  (полный уровень, без `TOP`, без кейсета);
- когда `PageSize` задан:
  - `SELECT TOP (@pageSize + 1) ...` (или `TOP (@pageSizePlusOne)` — см. ловушку 2);
  - в `WHERE` добавить `AND s.[L] > @cursor`, **только если** `Cursor` задан (первая порция —
    без предиката курсора);
  - `ORDER BY s.[L]` (уже так по умолчанию для NestedSet — подтвердить, не дублировать).

Псевдокод фрагмента:

```csharp
if (src.PageSize is not null)
    sb.Insert(afterSelectKeyword, $" TOP (@{PageSizeParam} + 1)");   // способ — см. ловушку 2

// ... существующий диапазон [L]>@left AND [R]<@right (+ NOT EXISTS / level) ...

if (src.PageSize is not null && src.Cursor is not null)
    sb.Append(" AND s.[").Append(src.Schema.LeftColumn).Append("] > @").Append(CursorParam);
```

Константы `PageSizeParam = "pageSize"`, `CursorParam = "cursor"` — рядом с прочими.

**Корневой уровень не пагинируется** в этой серии (корней немного) — `TOP`/кейсет добавлять
только в не-корневую ветку. Отметить; если понадобится — отдельная задача.

## 4. Параметры и загрузка

`ClayTreeData.BuildParams` (NestedSet): если `src.PageSize` задан — добавить `@pageSize`
(значение `PageSize`) и, если `Cursor` задан, `@cursor` (значение `Cursor`). Тип `@cursor` —
как у `L` в `ClayTreeRow` (`long`).

`ClayTreeData.LoadLevelAsync` / `ClaySqlTreeDataSource.LoadLevelAsync`: результат теперь несёт
не только узлы, но и признак «есть ещё» и новый курсор. Расширить `ClayTreeLoadResult`:

```csharp
/// <summary>Есть ли ещё непрочитанные дети уровня (пришло больше PageSize).</summary>
public bool HasMore { get; init; }

/// <summary>Курсор для следующей порции — L последнего ВОЗВРАЩАЕМОГО (не лишнего) ребёнка.</summary>
public long? NextCursor { get; init; }
```

Логика «есть ещё»: запросили `TOP(@n+1)`, пришло `rows`. Если `rows.Count > n` → `HasMore=true`,
**лишнюю (n+1-ю) строку отбросить**, `NextCursor` = `L` n-й (последней показанной). Если
`rows.Count ≤ n` → `HasMore=false`, `NextCursor` = `L` последней (или прежний курсор, неважно —
догрузок больше не будет).

## 5. `EnsureChildrenLoadedAsync` — дописывание

Сейчас метод грузит **весь** уровень: `Children.Clear(); Children.AddRange(...)`. Развести на
две операции, не ломая существующее поведение (без пагинации всё как было):

- **первая загрузка** узла (`!IsLoaded`): как сейчас, но если `LevelPageSize>0` и режим
  NestedSet — запросить первую порцию (`Cursor=null`), заполнить `Children`, выставить
  `LoadedAllChildren = !result.HasMore`, `LastChildCursor = result.NextCursor`;
- **догрузка порции** — новый метод `LoadMoreChildrenAsync(node)`:
  - `if (node.LoadedAllChildren || node.IsLoading) return;`
  - `IsLoading=true; StateHasChanged();`
  - запрос со `Cursor = node.LastChildCursor`, `PageSize = LevelPageSize`;
  - `Children.AddRange(result.Nodes)` (**дописать**, не чистить), `IndexNodes(result.Nodes, node)`;
  - `LoadedAllChildren = !result.HasMore; LastChildCursor = result.NextCursor;`
  - `IsLoading=false;` ошибки — в `_error`/`OnLoadError`, как в существующем методе.

Когда `LevelPageSize=0` или ParentKey — первая загрузка тянет уровень целиком,
`LoadedAllChildren=true`, `LoadMoreChildrenAsync` — no-op. Ноль регрессии.

`ExpandAsync`/`ReloadAsync` не меняются по смыслу; при `Reload` `LastChildCursor`/
`LoadedAllChildren` сбрасываются вместе с `Children` (проверить, что `Clear` их не оставляет
в старом состоянии).

## Ловушки

1. **`Cursor` только со второй порции.** Первая порция — без `AND [L] > @cursor` (иначе
   пропустишь самого левого ребёнка). Предикат курсора добавляется, только если `Cursor` задан.
2. **`TOP (@param + 1)` в T-SQL.** `SELECT TOP (@n + 1)` — валидный синтаксис на 2008 R2
   (скобки обязательны, выражение допускается). Если билдер вставляет `TOP` строкой — вставить
   именно `TOP (@pageSize + 1)`, не считать `n+1` в C# и не подставлять литерал (значение
   параметризовано). Проверить, что скобки на месте.
3. **`ORDER BY [L]` обязателен и стабилен.** Кейсет без сортировки по тому же ключу
   недетерминирован. Убедиться, что не-корневая ветка сортирует по `[L]`, а не по `OrderBy` из
   опций (в NestedSet порядок = `L`). Если `BuildOrderBy` может вернуть иное — для пагинируемого
   уровня форсировать `[L]`.
4. **Тип курсора.** `L` в `ClayTreeRow.Left` — `long?`. `@cursor` передавать как `long`.
   Не строкой (иначе сравнение `>` в SQL сломается на типизации).
5. **`HasChildren` и пустой уровень.** Если у узла `HasChildren=true`, но первая порция пуста
   (данные разошлись) — `LoadedAllChildren=true`, догрузки нет, не зациклиться.
6. **MARS.** Догрузка — последовательная, как и всё дерево. Не параллелить.

## Не делай

- Не пагинируй ParentKey и корневой уровень.
- Не вводи `OFFSET/FETCH`, `ROW_NUMBER`, CTE.
- Не делай составной курсор — только `L`.
- Не трогай UI (кнопка/скролл) — это CTP2; здесь `LoadMoreChildrenAsync` вызывается
  программно/тестом.
- Не меняй поведение при `LevelPageSize=0` — строго как сейчас.
- Не выполняй SQL в Controls; не создавай `DbManager`.

## Проверка

**Тесты `ClayTreeSqlBuilderTests`:**
- NestedSet, `PageSize` задан, `Cursor=null` (первая порция): SQL содержит `TOP (@pageSize + 1)`,
  сортировку по `[L]`, **нет** `[L] > @cursor`;
- NestedSet, `PageSize` и `Cursor` заданы: SQL содержит и `TOP (@pageSize + 1)`, и
  `[L] > @cursor`;
- `PageSize=null`: SQL как прежде — нет `TOP`, нет `@cursor` (существующие тесты не падают);
- ParentKey: `PageSize` игнорируется — нет `TOP`/`@cursor` даже если задан;
- нет `OFFSET`/`ROW_NUMBER`; идентификаторы в скобках; курсор — параметр `@cursor`, не литерал.

**Ручной прогон** (`/tree-test`, NestedSet, временно `LevelPageSize=3` на стенде, узел с >3
детьми; `LoadMoreChildrenAsync` дёрнуть временной отладочной кнопкой):
- первая порция — 3 ребёнка, `LoadedAllChildren=false`;
- вызов догрузки → дописались следующие 3, порядок по `L` продолжается, дублей/пропусков нет;
- последняя порция → `LoadedAllChildren=true`;
- в профайлере на каждую порцию один запрос с `[L] > @cursor`;
- `LevelPageSize=0` → весь уровень сразу, `LoadedAllChildren=true`;
- ParentKey с `LevelPageSize=3` → уровень целиком, догрузки нет;
- `dotnet build` + `dotnet test` — зелёные; отладочную кнопку убрать.
