# GA1 — Инъекция в ORDER BY / GROUP BY: сортировка и группировка из shared-ссылки не валидируются

## Контекст

`ClayDataQuery.BuildOrderBy` и `ClayGroupingEngine.BuildGroupAggregateSql` подставляют имена
колонок сортировки/группировки в SQL **текстом** (`$"{s.Column} DESC"`, `$"GROUP BY {grp}"`).
Это допустимо, только если имена гарантированно из доверенного источника (определение грида).

Дыра — режим общих настроек (share). Поток:
`LoadAndValidateSharedParamsAsync` → `ClaySharedParamValidator.IsValid` (проверяет **только имена
параметров**, напр. `Sort_123`, но не их значения) → `ApplySharedParams` → `ApplySavedSort(srtVal)`.
`ApplySavedSort` кладёт результат `GridStateSerializer.DeserializeSort` прямо в `_sortState`
**без сверки с `_columnBySqlName`**. Значение `srtVal` полностью контролируется автором ссылки.
Итог: открыв чужую ссылку вида `?...&sharedId=42`, где в параметре сортировки лежит
`1); DROP TABLE ... --:asc`, жертва выполняет произвольный SQL в своём подключении.

Группировка (`ApplySavedGroups`) фильтрует по `_columnBySqlName.ContainsKey` — но это не place
безопасности: она молча выкидывает неизвестные имена, а мы хотим единообразную, явную валидацию
для sort/group/URL из любого источника, и чтобы `BuildOrderBy` был безопасен сам по себе
(defense in depth), а не полагался на вызывающего.

Исправление в два уровня:
1. **Билдер защищается сам** — `BuildOrderBy` принимает белый список допустимых выражений и
   отбрасывает всё, чего в нём нет.
2. **Компонент валидирует сортировку при применении** — `ApplySavedSort` сверяется с
   `_columnBySqlName` (по `SortName`), как это уже делает `ApplySavedGroups` по `SqlName`.

Порядок файлов: Entities-подобный `ClayDataQuery` (в проекте Controls, но чистый) → компонент.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayDataQuery.cs`

**1.1.** Изменить сигнатуру `BuildOrderBy`, добавив необязательный белый список:

```csharp
public string BuildOrderBy(string defaultOrder, ISet<string>? allowedColumns = null)
```

**1.2.** В теле метода, в обеих ветках, где в `clauses` добавляется выражение из `GroupColumns`
или `SortColumns`, добавить проверку перед `clauses.Add(...)`. Ввести локальный помощник в начало
метода:

```csharp
bool Allowed(string col) => allowedColumns is null || allowedColumns.Contains(col);
```

и обернуть каждое добавление:
- в блоке группировки: `if (sortCol is not null) { if (Allowed(gc)) clauses.Add($"{gc} {...}"); } else if (Allowed(gc)) clauses.Add(gc);`
- в блоке `SortColumns.Count == 0` (разбор `defaultOrder`): `defaultOrder` — из определения,
  проверять не обязательно, но для единообразия оставить `clauses.Add(col)` без проверки
  (это доверенная строка). **Комментарием пометить**, что `defaultOrder` доверенный.
- в блоке `SortColumns` (пользовательская сортировка): `if (Allowed(s.Column)) clauses.Add($"{s.Column} {...}");`

**1.3.** После сборки `clauses`: если после фильтрации список **пуст** (все выражения отсеяны),
вернуть `defaultOrder`, а не пустую строку (иначе получим `ORDER BY ` без аргументов —
синтаксическая ошибка). В конец метода:

```csharp
return clauses.Count > 0 ? string.Join(", ", clauses) : defaultOrder;
```

### Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.cs`

**2.1.** В `ApplySavedSort` отфильтровать неизвестные колонки. Заменить тело:

```csharp
private void ApplySavedSort(string value)
{
    var sort = GridStateSerializer.DeserializeSort(value);
    if (sort.Count == 0) return;

    // Белый список SortName всех зарегистрированных колонок — защита от инъекции
    // через shared-ссылку (значение параметра контролируется автором ссылки).
    var allowedSortNames = _columnBySqlName.Values
        .Select(m => m.SortName)
        .ToHashSet(StringComparer.Ordinal);

    _sortState.Clear();
    foreach (var s in sort)
    {
        if (allowedSortNames.Contains(s.Column))
            _sortState.Add(s);
    }
}
```

**2.2.** Собрать белый список выражений сортировки и передать его в `BuildOrderBy` во всех местах
динамического режима. Найти вызовы `query.BuildOrderBy(_opt.DefaultOrder)`:
- в `LoadDynamicFlatData`;
- в `LoadDynamicGroupedData` (файл `ClayGrid.Dynamic.Grouping.cs`).

Ввести приватное свойство-хелпер в `ClayGrid.Dynamic.cs`:

```csharp
/// <summary>Белый список выражений, допустимых в ORDER BY: SortName всех колонок + IdColumn при наличии.</summary>
private ISet<string> AllowedOrderExpressions()
    => _columnBySqlName.Values.Select(m => m.SortName).ToHashSet(StringComparer.Ordinal);
```

и заменить оба вызова на `query.BuildOrderBy(_opt.DefaultOrder, AllowedOrderExpressions())`.

### Шаг 3 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.Grouping.cs`

Перед вызовом `BuildGroupAggregateSql` отфильтровать `exprs` по белому списку колонок
группировки (SqlName зарегистрированных группируемых колонок). В начало `LoadDynamicGroupedData`,
сразу после `var exprs = query.GroupColumns.ToList();`, добавить:

```csharp
// Защита: в GROUP BY попадают только выражения зарегистрированных группируемых колонок.
var groupableSql = _columnBySqlName.Values
    .Where(m => m.Groupable)
    .Select(m => m.SqlName)
    .ToHashSet(StringComparer.Ordinal);
exprs = exprs.Where(e => groupableSql.Contains(e)).ToList();
if (exprs.Count == 0)
{
    // все группы отсеяны — вырождаемся в плоский режим
    await LoadDynamicFlatData(query, where, dp);
    return;
}
```

## Критерии приёмки

- Юнит-тест `BuildOrderBy`: `SortColumns = [("Name; DROP", false)]`, `allowedColumns = {"Name"}`
  → результат равен `defaultOrder` (инъекция отсеяна, ORDER BY не пуст).
- Юнит-тест `ApplySavedSort` (через internal-доступ или обёртку): значение
  `"EvilExpr; DROP:asc"` при отсутствии такой колонки → `_sortState` пуст.
- Интеграционный: shared-ссылка с подделанным параметром сортировки/группировки не влияет на SQL
  (в сгенерированном ORDER BY/GROUP BY только известные колонки).
- Существующие снапшот-тесты SQL группировки (`GN*`) проходят без изменений для валидных колонок.
