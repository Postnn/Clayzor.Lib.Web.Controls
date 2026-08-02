# TA4 — Источники данных: подмена DataSource, потеря ExtraWhere при пагинации, гонки и отмена

## Контекст

Четыре дефекта:

1. **`ResolveDataSourceForNode` (ClayTreeView.Loading.cs) игнорирует параметр `DataSource`.**
   Если страница/тест передали свой источник, но включена пагинация (`LevelPageSize > 0`,
   NestedSet), метод молча создаёт `ClaySqlTreeDataSource` и лезет в БД мимо подменного источника.
2. **Там же теряется `ExtraWhere`.** `pageSource` собирается из `Options.*` заново, без
   `ExtraWhere`/`ExtraWhereParams` из `_source` → порционные загрузки уровня игнорируют
   дефолтный фильтр (первая порция и «Загрузить ещё» возвращают отфильтрованные и
   неотфильтрованные данные вперемешку).
3. **`EnsureChildrenLoadedAsync` не защищён от повторного входа.** Двойной клик по шеврону →
   две параллельные загрузки одного уровня (в `LoadMoreChildrenAsync` guard `if (node.IsLoading) return;`
   есть, здесь — нет).
4. **`ClaySqlTreeDataSource` глотает отмену.** `catch (Exception ex)` перехватывает и
   `OperationCanceledException` — отменённая операция отображается пользователю как ошибка загрузки.
   Попутно: `MapRow` в `ClayTreeData` конвертирует `Left`/`Right`/`Level` только из `int`/`long`
   (`l as long? ?? (l is int li ? li : null)`) — колонки `decimal`/`smallint`/`byte` дают `null`,
   и NestedSet перестаёт работать.

Порядок файлов: Entities → Controls.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Entities/Tree/ClayTreeData.cs`

В `MapRow` заменить три строки конверсии:

```csharp
if (row.TryGetValue(ClayTreeSqlBuilder.AliasLeft, out var l)) r.Left = l as long? ?? (l is int li ? li : null);
if (row.TryGetValue(ClayTreeSqlBuilder.AliasRight, out var ri)) r.Right = ri as long? ?? (ri is int rii ? rii : null);
if (row.TryGetValue(ClayTreeSqlBuilder.AliasLevel, out var lv)) r.Level = lv as int? ?? (lv is long ll ? (int?)ll : null);
```

на:

```csharp
if (row.TryGetValue(ClayTreeSqlBuilder.AliasLeft, out var l) && l is not null && l is not DBNull)
    r.Left = Convert.ToInt64(l);
if (row.TryGetValue(ClayTreeSqlBuilder.AliasRight, out var ri) && ri is not null && ri is not DBNull)
    r.Right = Convert.ToInt64(ri);
if (row.TryGetValue(ClayTreeSqlBuilder.AliasLevel, out var lv) && lv is not null && lv is not DBNull)
    r.Level = Convert.ToInt32(lv);
```

### Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/DataSources/ClaySqlTreeDataSource.cs`

В ОБОИХ методах (`LoadFilteredAsync` и `LoadLevelAsync`) перед `catch (Exception ex)` добавить:

```csharp
catch (OperationCanceledException)
{
    throw; // отмена — не ошибка данных, наверх без упаковки в Error
}
```

### Шаг 3 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Loading.cs`

**3.1.** В `EnsureChildrenLoadedAsync` после существующих ранних выходов добавить guard:

```csharp
if (node.IsLoaded) return;
if (!node.HasChildren) return;
if (node.IsLoading) return;   // ← добавить: защита от повторного входа (двойной клик)
```

**3.2.** Переписать `ResolveDataSourceForNode`:

```csharp
private IClayTreeDataSource ResolveDataSourceForNode(ClayTreeNode node, long? cursor)
{
    // Подменный источник (тесты, нестандартные данные) всегда главнее пагинации.
    if (DataSource is not null)
        return DataSource;

    if (Options.LevelPageSize <= 0 || Options.HierarchyMode != ClayTreeHierarchyMode.NestedSet)
        return _dataSource;

    // Кейсет-источник строится ИЗ _source, чтобы не потерять ExtraWhere/ExtraWhereParams.
    var pageSource = _source! with
    {
        PageSize = Options.LevelPageSize,
        Cursor   = cursor,
    };

    return new ClaySqlTreeDataSource(ResolveDb(), pageSource);
}
```

Старую сборку `new ClayTreeSource(Options.SelectSql, ...)` удалить. `ClayTreeSource` — record,
выражение `with` доступно.

## Критерии приёмки

- Тест с fake-`IClayTreeDataSource` и `LevelPageSize = 5` (NestedSet): все загрузки уровня идут
  через fake, `ClaySqlTreeDataSource` не создаётся.
- Дерево с `FilterDefaults` и пагинацией: SQL порционной загрузки содержит `AND (` + ExtraWhere
  (проверить юнит-тестом `BuildLevelSql` для `pageSource`, полученного через `with`).
- Быстрый двойной клик по шеврону не приводит к двум запросам (лог/счётчик в fake-источнике).
- Юнит-тест `MapRow`: `Left` типа `decimal` (например `12m`) маппится в `12L`, `DBNull` → `null`.
