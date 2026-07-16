> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Требует выполненных **GG7** и **GF13_dynamic_row_selection.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG8 — tri-state чекбоксы заголовков групп в динамическом режиме

**Шаг необязательный.** Без него грид группируется, раскрывается и листается; нет только
чекбоксов у строк-заголовков групп (у строк детализации они есть — GF13). Если режим выбора
(`SelectVisible`) в динамических гридах не используется — пропусти этот шаг.

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGridPageBase.cs`, метод `IClayGridDataLoader.LoadGroupChildIdsAsync` —
  **эталон. Прочитай построчно, особенно как строится ключ параметра и как ключ группы
  разбирается обратно на уровни.**
- `Components/Grid/ClayGrid.Grouping.cs` — `_groupChildIds`, `GetChildIdsForGroup`,
  `LoadChildIdsForGroupsAsync`, `ClearGroupChildCache`, `ComputeGroupCheckState`,
  `OnGroupSelectAsync`, `OnHeaderSelectAllAsync` (найди их все, читай целиком).
- `Components/Grid/ClayGrid.Selection.cs` — `_selectedIds` (`HashSet<int>`),
  `TryGetSelectionId` (добавлен в GF13), `ComputeSelectAllState`, `IsHeaderIndeterminate`.
- `Components/Grid/ClayGrid.razor.cs` — место, где после загрузки данных догружаются
  `_groupChildIds` для новых `GroupHeaderRow` (около строки 235:
  `if (row is GroupHeaderRow gh && !_groupChildIds.ContainsKey(gh.FullKey))`).
- `Components/Grid/ClayGrid.Dynamic.cs` — `_dynamicDef.IdColumn`, `_dynamicKnownColumns`.
- `Components/Grid/ClayGrid.Dynamic.Grouping.cs` — GG2–GG7.
- `Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` — `QueryRowsAsync`.

## Задача

Чекбокс заголовка группы — tri-state: все потомки выбраны / никто / часть. Чтобы его посчитать
и чтобы клик по нему выбирал всю группу, гриду нужны ID ВСЕХ строк группы — включая те, что
не на текущей странице. Их лениво догружает `LoadChildIdsForGroupsAsync`:

```csharp
private async Task LoadChildIdsForGroupsAsync(List<string> fullKeys)
{
    if (DataLoader is null || fullKeys.Count == 0) return;
    var batch = await DataLoader.LoadGroupChildIdsAsync(fullKeys, _lastQuery);
    foreach (var kv in batch)
        _groupChildIds[kv.Key] = kv.Value;
}
```

В динамическом режиме `DataLoader` — `null`, метод молча выходит, `_groupChildIds` пуст,
`GetChildIdsForGroup` возвращает `null`, чекбоксов у групп нет.

Нужна та же загрузка внутри грида: для каждого `FullKey` — `SELECT {IdColumn} FROM ({SelectSql})`
с базовым `WHERE` + условиями по ключам группы.

## Изменить/создать

**1.** `ClayGrid.Dynamic.Grouping.cs` — сохранить контекст последнего запроса. `LoadGroupChildIdsAsync`
в статике получает `query` аргументом, в динамике проще сохранить готовые `where`/`dp`
последней загрузки. В `LoadDynamicGroupedData`, сразу после вычисления `exprs`:

```csharp
    /// <summary>WHERE последней групповой загрузки — для ленивой догрузки ID потомков групп.</summary>
    private string? _dynamicGroupWhere;

    /// <summary>Параметры последней групповой загрузки (@search + фильтр).</summary>
    private DynamicParameters? _dynamicGroupParams;

    /// <summary>Выражения группировки последней загрузки.</summary>
    private List<string> _dynamicGroupExprs = [];
```

и в начале `LoadDynamicGroupedData`:

```csharp
        var exprs = query.GroupColumns.ToList();

        _dynamicGroupWhere  = where;
        _dynamicGroupParams = dp;
        _dynamicGroupExprs  = exprs;
```

В `LoadDynamicFlatData` — обнулить (`_dynamicGroupWhere = null; _dynamicGroupParams = null;
_dynamicGroupExprs = [];`), рядом со сбросом `_dynamicGroupRoots`.

**2.** `ClayGrid.Dynamic.Grouping.cs` — загрузка ID потомков:

```csharp
    /// <summary>
    /// Загружает ID всех строк указанных групп (динамический режим).
    /// Аналог ClayGridPageBase.LoadGroupChildIdsAsync: тот же SQL, но SelectSql и колонка Id
    /// берутся из определения грида, а запрос идёт через DynamicSql.
    /// Строки, чей Id не приводится к int, пропускаются (см. TryGetSelectionId в GF13).
    /// </summary>
    private async Task<Dictionary<string, HashSet<int>>> LoadDynamicGroupChildIdsAsync(
        IReadOnlyList<string> groupFullKeys)
    {
        var result = new Dictionary<string, HashSet<int>>();

        var idColumn = _dynamicDef?.IdColumn;
        if (groupFullKeys.Count == 0
            || string.IsNullOrWhiteSpace(idColumn)
            || _dynamicGroupParams is null
            || _dynamicGroupExprs.Count == 0)
            return result;

        // Белый список: IdColumn приходит из справочника Запросы и подставляется в SQL текстом.
        if (!_dynamicKnownColumns.Contains(idColumn))
            return result;

        foreach (var fullKey in groupFullKeys)
        {
            var keys     = fullKey.Split('\u001F');
            var dp       = new DynamicParameters();
            dp.AddDynamicParams(_dynamicGroupParams);
            var keyParts = new List<string>();

            for (int i = 0; i < keys.Length && i < _dynamicGroupExprs.Count; i++)
            {
                var pName = $"gk_{fullKey.GetHashCode() & 0x7FFFFFFF}_{i}";
                dp.Add(pName, keys[i]);
                keyParts.Add($"{_dynamicGroupExprs[i]} = @{pName}");
            }

            if (keyParts.Count == 0) continue;

            var groupWhere    = string.Join(" AND ", keyParts);
            var combinedWhere = ClayDataQuery.CombineWhere(_dynamicGroupWhere, groupWhere);

            var sql = $"SELECT {idColumn} FROM ({SelectSql}) _src";
            if (!string.IsNullOrWhiteSpace(combinedWhere))
                sql += $" WHERE {combinedWhere}";

            var rows = await DynamicSql.QueryRowsAsync(Db, sql, dp);

            var ids = new HashSet<int>();
            foreach (var row in rows)
            {
                var raw = row.GetValueOrDefault(idColumn);
                if (raw is null or DBNull) continue;
                if (int.TryParse(raw.ToString(), out var id))
                    ids.Add(id);
            }

            result[fullKey] = ids;
        }

        return result;
    }
```

Разбор мест, где легко ошибиться:

- **`\u001F`** — разделитель уровней в `FullKey`, его ставит `ClayGroupingEngine.BuildAggregates`.
  Не выдумывай свой.
- **Ключи группы — это строки** (`FullKey` собран из `ToString()`), а исходные типы потеряны.
  Эталон передаёт их как строковые параметры и полагается на неявное приведение SQL Server
  (`КодТипа = @gk_..._0`, где параметр `nvarchar`). Делай так же — это осознанный компромисс
  эталона, не «улучшай» его типизацией.
- **`ids` может быть пустым множеством** — это НЕ то же самое, что «не загружено».
  `GetChildIdsForGroup` возвращает `null` для незагруженного и `HashSet` для загруженного;
  `result[fullKey] = ids` кладём всегда, даже пустой, иначе догрузка будет пытаться повторяться
  на каждый рендер.
- **`GetHashCode()`** в имени параметра — из эталона. Коллизия хешей двух `FullKey` в одном
  `dp` теоретически возможна, но `dp` здесь свой на каждую группу (в эталоне общий) — значит
  безопаснее эталона. Не упрощай до `$"gk_{i}"`: имя должно оставаться уникальным, если кто-то
  позже вернёт общий `dp`.
- **`_dynamicGroupParams` переиспользовать напрямую нельзя** — `dp.AddDynamicParams(...)` в
  новый экземпляр, иначе параметры групп накопятся в общем наборе.

**3.** `ClayGrid.Grouping.cs`, `LoadChildIdsForGroupsAsync` — диспетчер:

```csharp
    private async Task LoadChildIdsForGroupsAsync(List<string> fullKeys)
    {
        if (fullKeys.Count == 0) return;

        var batch = Dynamic
            ? await LoadDynamicGroupChildIdsAsync(fullKeys)
            : DataLoader is not null
                ? await DataLoader.LoadGroupChildIdsAsync(fullKeys, _lastQuery)
                : [];

        foreach (var kv in batch)
            _groupChildIds[kv.Key] = kv.Value;
    }
```

`ClearGroupChildCache()` (сброс кеша перед перезагрузкой) уже вызывается из общего кода и
режима не различает — проверь, что он действительно вызывается перед `LoadDynamicData`.
**Если нет — ОСТАНОВИСЬ и скажи**, не переставляй вызовы сам: кеш потомков, переживший смену
фильтра, даст неверный tri-state.

**4.** `ClayGrid.razor`, колонка выбора, ветка `context.Item is GroupHeaderRow` — НЕ трогать.
Она работает через `GetChildIdsForGroup`/`ComputeGroupCheckState`, а те после пункта 3
наполняются в обоих режимах.

## Не делай

Не меняй тип `_selectedIds` / `_groupChildIds` (`HashSet<int>`) — см. «Не делай» в GF13.
Не трогай `IClayGridDataLoader.LoadGroupChildIdsAsync` и его реализацию в `ClayGridPageBase`.
Не убирай белый список для `idColumn`. Не грузи ID всех групп разом при загрузке страницы —
загрузка ленивая, по факту появления `GroupHeaderRow` в `Items` (это уже сделано в
`ClayGrid.razor.cs`). Не трогай `ClayGroupingEngine`.

## Проверка (ручная)

Временно добавить в `Home.razor` `SelectVisible="true"`. `?id=140`, сгруппировать по
«Тип исследования», размер страницы 10:

- «Выбрать записи» → чекбоксы появились и у строк детализации, и у ЗАГОЛОВКОВ ГРУПП;
- профайлер: при появлении заголовков групп на странице ушли запросы
  `SELECT КодИсследования FROM (...) _src WHERE ... AND КодТипа = @gk_..._0` — по одному на
  группу, и только на новые группы;
- клик по чекбоксу группы → выбраны ВСЕ записи группы, включая те, что не на текущей странице:
  в меню групповых операций счётчик «Выбранные (N)» равен `ItemCount` группы;
- раскрыть группу → у всех её строк детализации чекбоксы отмечены;
- снять одну строку детализации → чекбокс группы стал indeterminate;
- отметить её обратно → чекбокс группы снова полностью отмечен;
- чекбокс «выделить всё» в шапке учитывает и группы, и детали (`ComputeSelectAllState`);
- сменить фильтр → выбор сброшен, кеш потомков сброшен, повторные запросы ушли заново с новым
  `WHERE` (`ClearGroupChildCache`);
- двухуровневая группировка: чекбокс ВНЕШНЕЙ группы выбирает все записи всех её подгрупп;
- перейти на страницу 2 и обратно → выбор сохранился, лишних запросов ID нет (кеш);
- негативный: временно поставить в `Запросы.ID` для грида 140 нечисловую колонку → чекбоксов
  нет ни у строк, ни у групп, грид не падает. Вернуть `КодИсследования`;
- **убрать `SelectVisible="true"` из `Home.razor`**, если он там не нужен постоянно.

Статический режим (`MedicalTests.razor`, `SelectVisible="true"`, с группировкой): чекбоксы
групп, tri-state, «выделить всё», печать и Excel выбранных — всё как раньше. Это главная
проверка шага: `LoadChildIdsForGroupsAsync` — общий метод.
