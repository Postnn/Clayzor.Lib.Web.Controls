# CGFR1 — Dynamic ClayGrid: корректная переинициализация одного экземпляра при смене identity

## Контракт проекта

На одной странице одновременно может существовать **не более одного `ClayGrid` в режиме `Dynamic`**.

CGFR1 НЕ должен проектироваться под несколько параллельных dynamic-grid экземпляров.

Не добавлять:

- глобальные registry dynamic grid;
- static caches для координации нескольких grid;
- per-grid coordinators;
- cross-instance synchronization;
- сложную generation infrastructure только ради multi-grid scenario.

Исправляется только lifecycle **одного экземпляра `ClayGrid<TEntity>`**.

---

# 1. Проблема

Сейчас dynamic initialization выполняется один раз:

```csharp
protected override async Task OnInitializedAsync()
{
    if (_opt.Dynamic && !_dynamicInitDone)
        await InitDynamicMode();
}
```

После этого:

```csharp
_dynamicInitDone = true;
```

При последующих parameter updates `OnParametersSet()` только делает:

```csharp
_opt = ResolveOptions();
```

и не проверяет, изменилась ли dynamic identity.

Если Blazor переиспользовал тот же экземпляр компонента при:

```text
GridId A -> GridId B
```

или изменился URL/query:

```text
?id=101 -> ?id=202
```

старый runtime state может остаться внутри компонента.

---

# 2. Stale query cache

Сейчас query string кэшируется:

```csharp
private NameValueCollection? _queryCache;

private NameValueCollection Query
    => _queryCache ??=
        HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query);
```

Этот cache не связан с текущим `Nav.Uri`.

После navigation тот же component instance может продолжать читать старые:

```text
GridId
CLID
sharedId
URL filters
URL column parameters
```

Это отдельный проявляющийся симптом той же lifecycle-проблемы.

---

# 3. Scope CGFR1

Исправить:

```text
same ClayGrid component instance
+ changed dynamic identity
=> full invalidation old dynamic runtime
=> initialization new dynamic identity
```

Под dynamic identity здесь понимаются:

```text
resolved GridId
resolved CLID
resolved sharedId
relevant ClayGridDynamicSettings
```

Не считать presentation-only настройки частью dynamic identity.

---

# 4. Минимальный lifecycle key

Добавить internal value-based key, например:

```csharp
internal readonly record struct ClayGridDynamicKey(...)
```

Имя можно изменить.

Key должен включать как минимум:

```text
GridId
CLID
sharedId
```

Также key должен учитывать application-level dynamic settings, изменение которых делает текущие definition/state caches невалидными.

Рассмотреть:

```text
ConnectionStringName
SettingsTable
ColumnsTable
UserParamsTable
UserSharedParamsTable
UserParamsShared

GridIdQueryParam
ClientIdQueryParam

ColumnsParamPrefix
FilterParamPrefix
GroupingParamPrefix
SortingParamPrefix
PageSizeParamPrefix
QuickSearchParamPrefix
```

Не включать автоматически все свойства `ClayGridOptions`.

Не включать presentation-only поля:

```text
Title
ShowAddButton
ShowPagination
ColumnMenuMode
AllowColumnReorder
ShowPrint
ShowExcel
EditSuccessMessage
```

---

# 5. Не использовать reference equality Options

Нельзя считать достаточным:

```csharp
if (!ReferenceEquals(_lastOptions, Options))
```

`ClayGridOptions` mutable и по текущему контракту обычно создаётся один раз.

Такой сценарий должен работать:

```csharp
var options = new ClayGridOptions
{
    Dynamic = true,
    DynamicGridId = 101
};

render(options);

options.DynamicGridId = 202;

rerender same component with SAME options instance;
```

Ожидание:

```text
dynamic reinitialization occurs
```

---

# 6. Query cache

Предпочтительно сделать query parser URI-aware.

Например:

```csharp
private string? _queryCacheUri;
private NameValueCollection? _queryCache;

private NameValueCollection Query
{
    get
    {
        if (!string.Equals(_queryCacheUri, Nav.Uri, StringComparison.Ordinal))
        {
            _queryCacheUri = Nav.Uri;
            _queryCache =
                HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query);
        }

        return _queryCache;
    }
}
```

Или вообще убрать persistent cache, если это проще.

Важно:

```text
Nav.Uri changed
=> Query MUST reflect current URI
```

Не использовать полный `Nav.Uri` как единственный lifecycle key.

Lifecycle key должен строиться из resolved identity values:

```text
GridId
CLID
sharedId
```

---

# 7. Lifecycle owner

Dynamic initialization больше не должна зависеть только от `OnInitializedAsync`.

Нужен path, который выполняется и при последующих parameter updates.

Предпочтительный вариант:

```text
OnParametersSetAsync
```

Схема:

```text
ResolveOptions
if static mode:
    static behavior
else:
    build current dynamic key
    compare with previous key
    if same:
        no reinit
    if changed:
        reset old dynamic runtime
        init new dynamic runtime
```

Не допустить двойной initial load из:

```text
OnInitializedAsync
+
OnParametersSetAsync
```

У dynamic initialization должен быть один понятный lifecycle owner.

---

# 8. Explicit reset method

Добавить единый helper:

```csharp
private void ResetDynamicRuntimeState()
```

или эквивалент.

Не разбрасывать invalidation по разным lifecycle methods.

Reset должен выполняться ДО initialization нового grid.

---

# 9. Что обязательно очистить

После смены identity не должны оставаться state/caches старого dynamic grid.

Обязательно рассмотреть очистку:

```text
_dynamicGridId
_dynamicClid

_dynamicDef
_dynamicCols
_dynamicKnownColumns

_dynamicLookups
_dynamicIconLookups

_dynamicEditUrl
_dynamicNewUrl
_dynamicDeleteSql

_dynamicSavedParams
_dynamicForcedParamNames
_dynamicQuickSearchCols
_quickSearchEffective

_defaultColumnOrder
_defaultHiddenNames

_dynamicError
_dynamicInitDone
```

Также definition-dependent column state:

```text
_columnById
_columnBySqlName
_columnOrder
_hiddenSqlNames
_cellTemplates
```

Иначе Grid B может получить смесь колонок A+B.

---

# 10. Query/UI state старого grid

При identity change старое состояние A не должно применяться к SQL/columns grid B.

Сбросить старые:

```text
_searchText
_sortState
_groupColumns
_filterRoot
_pageNumber
selection
dynamic expanded groups
group child id cache
value-filter overrides
```

Новый grid должен затем восстановить СВОЁ состояние обычным существующим pipeline.

Концептуальный порядок:

```text
reset A
load definition B
build B columns/defaults
restore B personal state
refresh B quick search
apply current B URL params
apply B shared state if present
load B data
```

---

# 11. Rows/count

В dynamic mode компонент сам владеет:

```csharp
Items
TotalCount
```

После начала перехода A -> B нельзя показывать rows A под definition/error B.

При reset dynamic runtime безопасно очистить:

```text
Items = []
TotalCount = 0
```

если это не затрагивает static mode.

---

# 12. Error state

Ошибка grid A:

```text
_dynamicError
```

не должна отображаться для B.

Reset должен очищать stale error до init B.

---

# 13. Action state

Обязательно очистить:

```text
_dynamicEditUrl
_dynamicNewUrl
_dynamicDeleteSql
```

Если A имел delete action, а B нет, кнопка удаления не должна остаться от A.

---

# 14. Lookup state

Обязательно очистить:

```text
_dynamicLookups
_dynamicIconLookups
```

Не переиспользовать lookup между grid IDs только потому, что совпало имя колонки.

---

# 15. Quick-search state

Очистить:

```text
_dynamicQuickSearchCols
_quickSearchEffective
```

до вычисления quick search нового grid.

---

# 16. Selection state

Старые selected IDs нельзя переносить между dynamic identities.

Минимум очистить:

```text
_selectedIds
_selectAllChecked
group child id cache
```

`_selectMode` можно оставить/сбросить по существующему UX contract, но selected records старого grid не должны сохраняться.

---

# 17. Group/filter/sort state

Перед init нового grid старые:

```text
sorting
grouping
filter
```

должны быть очищены.

Это correctness requirement.

Иначе grid B может получить SQL expression с колонкой, которая существовала только в A.

---

# 18. Same key — никакой переинициализации

Если lifecycle key не изменился:

```text
rerender
presentation-only Options change
ordinary StateHasChanged
```

НЕ должны повторно загружаться:

```text
definition
columns
lookups
saved params
first page data
```

Нельзя решить bug через:

```text
InitDynamicMode on every OnParametersSetAsync
```

---

# 19. GridId change

Обязательный сценарий:

```text
Dynamic=true
GridId=101
render
same component instance
GridId=202
rerender
```

Ожидание:

```text
definition 202 loaded
columns 202 loaded
data 202 loaded

no stale columns/lookups/actions/state from 101
```

---

# 20. URL GridId change

Обязательный regression для query cache.

Если `Options.DynamicGridId == null`:

```text
Nav.Uri = "...?id=101"
render

Nav.Uri = "...?id=202"
rerender SAME component instance
```

Ожидание:

```text
current resolved GridId == 202
```

Старый parsed query 101 не должен использоваться.

---

# 21. CLID change

Если:

```text
?CLID=1 -> ?CLID=2
```

при том же GridId, это новая persistence identity.

Ожидание:

```text
old _dynamicSavedParams not reused
state loaded for CLID 2
```

Key должен учитывать resolved CLID.

---

# 22. sharedId change

Сценарии:

```text
no sharedId -> sharedId=X
sharedId=X -> sharedId=Y
sharedId=X -> no sharedId
```

должны менять dynamic lifecycle identity.

Старый shared/personal state не должен смешиваться с новым.

---

# 23. Dynamic true/false

Проверить transitions:

```text
false -> true
true -> false
```

Минимум:

### false -> true

Dynamic initialization реально запускается.

### true -> false

старый dynamic runtime не должен продолжать управлять static grid.

Не переписывать static architecture.

---

# 24. `_dynamicInitDone`

После CGFR1 `_dynamicInitDone` может быть:

- удалён;
- оставлен.

Но он не должен означать:

```text
once initialized => forever initialized
```

Если lifecycle key изменился, новый init обязан выполняться.

---

# 25. Не усложнять concurrency

Известный контракт:

```text
на странице максимум один Dynamic ClayGrid
```

Поэтому НЕ добавлять без доказанной необходимости:

```text
SemaphoreSlim
global lock
generation manager
cross-instance cancellation coordinator
static synchronization
```

Если Blazor lifecycle текущего компонента сериализует parameter updates, опираться на это.

Только если в существующем коде реально виден race одного экземпляра:

```text
init A finishes after init B and overwrites B
```

добавить минимальную local protection.

Не проектировать multi-grid system.

---

# 26. Exception handling

CGFR1 не должен вводить новые blanket catches.

Не добавлять:

```csharp
catch { }
catch (Exception) { }
```

в lifecycle/reinitialization.

`OperationCanceledException` не проглатывать.

Exception boundary audit будет отдельно в CGFR2.

---

# 27. Tests — bUnit обязателен

В тестовом проекте уже есть bUnit.

Нужен component-level regression с реальным:

```text
ClayGrid<TEntity>
```

Не ограничиваться тестом key record.

---

# 28. Главный component test

Использовать ОДИН rendered component instance.

Концептуально:

```csharp
var cut = RenderComponent<ClayGrid<TestRow>>(p => ... A ...);

var instance = cut.Instance;

cut.SetParametersAndRender(p => ... B ...);

Assert.Same(instance, cut.Instance);
```

Далее доказать:

```text
B initialized
A state gone
```

---

# 29. Лучшее observable доказательство

Если test seam позволяет definition A/B:

Grid A:

```text
ColumnA
```

Grid B:

```text
ColumnB
```

После switch:

```text
ColumnB present
ColumnA absent
```

Это лучше, чем просто проверить приватный `_dynamicGridId`.

Тест должен доказывать реальный runtime invalidation.

---

# 30. Query cache regression

Добавить component/lifecycle test:

```text
initial URL:
?id=101&CLID=1

same component:
?id=202&CLID=2
```

Ожидание:

```text
new identity resolves 202 / 2
```

Не сбрасывать `_queryCache` вручную из test.

---

# 31. Same identity does not reload

Обязательный anti-regression test:

```text
render grid 101
rerender same component with identity 101
change only presentation option
```

Ожидание:

```text
dynamic initialization count == 1
```

---

# 32. Same mutable Options instance

Очень желательно сделать отдельный test:

```csharp
var options = new ClayGridOptions
{
    Dynamic = true,
    DynamicGridId = 101
};

render(options);

options.DynamicGridId = 202;

rerender(options);
```

Ссылка `Options` та же.

Ожидание:

```text
reinit
```

---

# 33. Key unit tests

Добавить узкие unit tests:

```text
same values -> equal
GridId changed -> not equal
CLID changed -> not equal
sharedId changed -> not equal
relevant dynamic settings changed -> not equal
presentation-only settings do not participate
```

Но эти тесты НЕ заменяют bUnit lifecycle test.

---

# 34. Test seam

Не подключаться к реальному SQL Server.

Разрешено добавить минимальный internal seam для orchestration.

Предпочтения:

1. existing abstractions/fakes;
2. internal helper;
3. internal virtual test seam;
4. `InternalsVisibleTo`.

Не добавлять public test-only API:

```text
public CurrentDynamicKey
public ResetForTests
public QueryCache
```

---

# 35. Initial-load contract

Обычный initial dynamic render после CGFR1 должен по-прежнему выполнить один раз:

```text
resolve identity
load definition
load columns
restore personal state
refresh quick search
apply URL params
apply shared params
load data
```

Не должно быть двойного initial data load.

---

# 36. Static mode

Static `ClayGrid` не должен:

```text
читать dynamic DB
требовать GridId
переинициализироваться на каждом render
терять статические column registrations
```

CGFR1 не должен ломать static behavior.

---

# 37. Не менять persistence format

Не менять:

```text
GridStateSerializer
ClayGridParamRegistry
saved param names
shared param payload format
```

---

# 38. Не менять SQL

Не переписывать:

```text
DynamicSql
grouping SQL
filter SQL
quick search SQL
paging SQL
export SQL
delete SQL
```

CGFR1 — lifecycle/invalidation task.

---

# 39. AGENTS.md

Обновить:

```text
Components/Grid/AGENTS.md
```

Зафиксировать:

```text
На странице допускается максимум один ClayGrid в Dynamic mode.

Dynamic runtime state одного компонента привязан к value-based lifecycle identity:
GridId + CLID + sharedId + relevant dynamic settings.

При смене identity старое definition-dependent состояние полностью инвалидируется
до initialization нового grid.

NavigationManager.Uri change не может оставлять stale parsed query.

Same identity rerender не вызывает повторную dynamic initialization.
```

---

# 40. Что НЕ принимать

Не принимать только:

```csharp
_queryCache = null;
```

Не принимать только:

```csharp
_dynamicInitDone = false;
```

Не принимать reference equality `Options`.

Не принимать full reinit на каждый render.

Не принимать решение, рассчитанное на несколько parallel dynamic grids и добавляющее лишнюю infrastructure.

Не принимать тест:

```text
dispose component A
create component B
```

Нужен SAME component instance.

Не принимать только key unit tests без component lifecycle test.

---

# 41. Acceptance criteria

CGFR1 считается выполненным, если:

## Runtime

- [ ] На странице по контракту поддерживается один Dynamic ClayGrid.
- [ ] Есть value-based dynamic lifecycle key или эквивалент.
- [ ] Key учитывает GridId.
- [ ] Key учитывает CLID.
- [ ] Key учитывает sharedId.
- [ ] Relevant dynamic settings учитываются.
- [ ] `Nav.Uri` change не оставляет stale Query cache.
- [ ] Same component instance A -> B выполняет reinit.
- [ ] Старые columns полностью удаляются.
- [ ] Старые lookups полностью удаляются.
- [ ] Старые action URLs/delete SQL удаляются.
- [ ] Старые filter/group/sort/selection state не применяются к B.
- [ ] Старые rows/count не отображаются как B.
- [ ] Same identity rerender не вызывает reinit.
- [ ] Initial render не вызывает double initialization/data load.
- [ ] Static mode не сломан.

## Tests

- [ ] Есть bUnit test SAME component instance A -> B.
- [ ] Test реально доказывает invalidation runtime state.
- [ ] Есть URL/query regression.
- [ ] Есть same-key no-reinit regression.
- [ ] Есть unit tests key equality.
- [ ] Желательно есть same mutable Options instance test.

## Hygiene

- [ ] Нет public test-only API.
- [ ] Нет real SQL dependency.
- [ ] Нет blanket catches.
- [ ] Нет magic `Task.Delay`.
- [ ] Existing tests green.
- [ ] `Components/Grid/AGENTS.md` обновлён.

---

# 42. Проверка

Перед завершением:

```bash
dotnet test
```

для тестового проекта.

Также:

```bash
dotnet build
```

основной библиотеки.

В отчёте указать:

1. production files;
2. test files;
3. состав lifecycle key;
4. как исправлен Query cache;
5. что очищает reset;
6. какой bUnit test доказывает same-instance A -> B;
7. как доказано отсутствие reinit при same key;
8. build/test result.

---

# 43. Коммит

Один отдельный commit только для CGFR1.

Предлагаемый message:

```text
CGFR1: reinitialize dynamic grid on identity change
```

Не смешивать CGFR2 exception handling и другие исправления.
