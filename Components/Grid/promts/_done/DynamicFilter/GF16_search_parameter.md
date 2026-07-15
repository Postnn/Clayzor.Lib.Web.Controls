> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненных **GF1**, **GF2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF16 — потерянный параметр `@search`: поиск в динамическом гриде падает

Прочитать перед началом: `Components/Grid/ClayDataQuery.cs` — `BuildWhereClause` (что именно
он генерирует), `CombineWhere`; `Components/Grid/ClayGrid.Dynamic.cs` — `LoadDynamicData`;
`Components/Grid/ClayGridPageBase.cs` — `LoadFlatData` и `LoadGroupedData` (как то же самое
сделано в статическом режиме); `Components/Grid/ClayGrid.Search.cs`.

## Дефект

**Найден при разборе группировки, в `GF7_backlog.md` его не было.**

`ClayDataQuery.BuildWhereClause` генерирует SQL с именованным параметром:

```csharp
public string? BuildWhereClause(params string[] searchColumns)
{
    if (string.IsNullOrWhiteSpace(SearchText) || searchColumns.Length == 0)
        return null;

    return string.Join(" OR ", searchColumns.Select(c => $"{c} LIKE @search"));
}
```

Сам параметр метод не добавляет — это обязанность вызывающего. Статический режим её выполняет,
`ClayGridPageBase.LoadFlatData`:

```csharp
var searchWhere    = _query.BuildWhereClause(searchColumns);
var dp             = new DynamicParameters();
dp.Add("search", $"%{_query.SearchText}%");     // ← вот это
var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
```

`LoadDynamicData` — не выполняет:

```csharp
var dp = new DynamicParameters();

var searchWhere = query.BuildWhereClause(SearchColumns);
var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
var where       = ClayDataQuery.CombineWhere(searchWhere, filterWhere);
```

`dp.Add("search", ...)` нет нигде. Пока поле поиска пустое, `BuildWhereClause` возвращает `null`
и всё работает. Как только пользователь введёт хоть один символ, в SQL уедет `... LIKE @search`
без объявленного параметра → `SqlException` «Must declare the scalar variable "@search"».
`DbManager` передаст её в `ISqlErrorHandler` → `ClayErrorService`, данные не загрузятся.

Проценты вокруг значения (`$"%{...}%"`) добавляет вызывающий, а не `BuildWhereClause` — в
шаблоне только `LIKE @search`. Скопируй формат статического режима один в один, иначе поиск
будет искать точное совпадение вместо вхождения.

## Изменить/создать

`ClayGrid.Dynamic.cs`, начало `LoadDynamicData`:

```csharp
private async Task LoadDynamicData(ClayDataQuery query)
{
    var dp = new DynamicParameters();

    // BuildWhereClause генерирует "col LIKE @search", но параметр не добавляет —
    // это делает вызывающий (ср. ClayGridPageBase.LoadFlatData).
    dp.Add("search", $"%{query.SearchText}%");

    var searchWhere = query.BuildWhereClause(SearchColumns);
    var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
    var where       = ClayDataQuery.CombineWhere(searchWhere, filterWhere);

    /* … дальше как было … */
}
```

Параметр добавляется безусловно, даже когда `SearchText` пуст: лишний неиспользуемый параметр
SQL Server не смущает, а условная логика — лишний шанс ошибиться. Статический режим делает
ровно так же.

## Не делай

Не меняй `ClayDataQuery.BuildWhereClause` — на его контракт («генерирую SQL, параметр
добавляешь ты») завязан статический режим и тесты. Не переименовывай параметр `search`.
Не убирай `%`-обрамление. Не трогай `ClayCompositeSqlBuilder` — он свои параметры в `dp`
добавляет сам.

## Проверка (ручная)

- `?id=140` → ввести в поле «Поиск...» подстроку, которая есть в названии одной записи →
  грид показал только её, «Всего: 1 записей», ошибок нет;
- ввести подстроку из середины значения (не с начала) → запись находится (`%...%`, а не `...%`);
- очистить поиск → вернулись все записи;
- поиск + фильтр одновременно → работают вместе (`CombineWhere` через AND);
- поиск + переход на страницу 2 → поиск не потерялся;
- в SQL-профайлере: в `sp_executesql` объявлен `@search nvarchar(...)` со значением
  `%подстрока%`;
- статический режим (`MedicalTests.razor`): поиск работает как раньше.
