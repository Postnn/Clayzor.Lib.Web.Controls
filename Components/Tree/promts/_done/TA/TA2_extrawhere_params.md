# TA2 — ExtraWhere без параметров: дефолтный фильтр падает на «Must declare the scalar variable @p0»

## Контекст

`ClayCompositeSqlBuilder.Build` возвращает **параметризованный** фрагмент WHERE (`[Col] LIKE @p0`)
и складывает значения в `DynamicParameters`. Дерево использует этот фрагмент как
`ClayTreeSource.ExtraWhere` для «тихого» дефолтного фильтра, но **значения параметров выбрасывает**:

- `ClayTreeView.razor.cs`, `OnInitializedAsync`: `UpdateSourceExtraWhere(BuildDefaultWhere().whereClause);`
- `ClayTreeView.Filter.cs`, `ApplyFilterAsync` (дефолтный режим): `var (defWhere, _) = BuildDefaultWhere();`

`ClayTreeData.BuildParams` про эти параметры не знает → любой запрос уровня с `ExtraWhere`
падает в SQL Server с ошибкой «Must declare the scalar variable @p0». Т.е. режим `FilterDefaults`
неработоспособен целиком.

Исправление: `ClayTreeSource` получает словарь параметров ExtraWhere, `ClayTreeData` добавляет их
в каждый запрос уровня, компонент перестаёт выбрасывать параметры.

Порядок файлов: сначала Entities (модель и данные), затем Controls (компонент).

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Entities/Tree/ClayTreeSource.cs`

Добавить в record последний позиционный параметр:

```csharp
public sealed record ClayTreeSource(
    string SelectSql,
    ClayTreeHierarchyMode Mode,
    ClayTreeSchema Schema,
    string? OrderBy = null,
    object? RootId = null,
    int? PageSize = null,
    long? Cursor = null,
    string? ExtraWhere = null,
    IReadOnlyDictionary<string, object?>? ExtraWhereParams = null);
```

В XML-doc записи добавить: «`ExtraWhereParams` — значения Dapper-параметров, на которые ссылается
`ExtraWhere` (имена без `@`). Задаются всегда вместе с `ExtraWhere`.»

### Шаг 2 — файл `Clayzor.Lib.Entities/Tree/ClayTreeData.cs`

В конец метода `BuildParams(ClayTreeSource src, ClayTreeRow? parent)`, перед `return dp;`, добавить:

```csharp
// Параметры ExtraWhere (дефолтный фильтр): добавляются в каждый запрос уровня
if (src.ExtraWhereParams is { Count: > 0 })
{
    foreach (var (name, value) in src.ExtraWhereParams)
        dp.Add(name, value);
}
```

### Шаг 3 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Filter.cs`

**3.1.** Изменить сигнатуру и возврат `BuildDefaultWhere`: вместо
`(string? whereClause, DynamicParameters? dp)` возвращать
`(string? whereClause, IReadOnlyDictionary<string, object?>? prms)`.
В конце метода заменить:

```csharp
var dp = new DynamicParameters();
var whereClause = ClayCompositeSqlBuilder.Build(defRoot, dp, knownColumns);
return (whereClause, dp);
```

на:

```csharp
var dp = new DynamicParameters();
var whereClause = ClayCompositeSqlBuilder.Build(defRoot, dp, knownColumns);
if (whereClause is null)
    return (null, null);

var prms = new Dictionary<string, object?>();
foreach (var name in dp.ParameterNames)
    prms[name] = dp.Get<object?>(name);
return (whereClause, prms);
```

**3.2.** Изменить сигнатуру `UpdateSourceExtraWhere(string? extraWhere)` на
`UpdateSourceExtraWhere(string? extraWhere, IReadOnlyDictionary<string, object?>? extraWhereParams)`
и передавать оба значения в конструктор `ClayTreeSource` (девятым аргументом `extraWhereParams`).
Условие раннего выхода оставить по `ExtraWhere` (`if (_source.ExtraWhere == extraWhere) return;`).

**3.3.** Обновить все вызовы в этом файле:
- дефолтный режим `ApplyFilterAsync`:
  ```csharp
  var (defWhere, defParams) = BuildDefaultWhere();
  UpdateSourceExtraWhere(defWhere, defParams);
  ```
- ветки сброса: `UpdateSourceExtraWhere(null, null);` (две штуки — в пустом и пользовательском режимах).

### Шаг 4 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.razor.cs`

В `OnInitializedAsync` заменить:

```csharp
if (_isDefaultOnly)
    UpdateSourceExtraWhere(BuildDefaultWhere().whereClause);
```

на:

```csharp
if (_isDefaultOnly)
{
    var (defWhere, defParams) = BuildDefaultWhere();
    UpdateSourceExtraWhere(defWhere, defParams);
}
```

(Этот блок будет ещё раз затронут в TA3 — не пропускать текущий шаг, TA3 опирается на него.)

## Критерии приёмки

- Проект собирается; других вызовов `UpdateSourceExtraWhere`/`BuildDefaultWhere` в решении нет
  (проверить поиском по всему решению).
- Тест: дерево с `FilterDefaults = { ["Name"] = "abc" }`, режим ParentKey, fake-источник не нужен —
  юнит-тест на `ClayTreeData`-уровне: `BuildParams` для источника с
  `ExtraWhere = "[Name] LIKE @p0"`, `ExtraWhereParams = { ["p0"] = "%abc%" }`
  возвращает `DynamicParameters`, где `ParameterNames` содержит `p0`.
- Тест на `BuildLevelSql` + `ExtraWhere` (существующий) продолжает проходить.
