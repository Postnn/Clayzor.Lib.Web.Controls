# TA3 — Жизненный цикл: начальная загрузка, применение URL-фильтра, OnParametersSetAsync

## Контекст

Файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.razor.cs`. Три дефекта:

1. **Начальная загрузка не выполняется.** `OnInitializedAsync` строит `_source`, разбирает фильтры,
   но не вызывает ни `LoadRootsAsync`, ни `ApplyFilterAsync`. `OnParametersSetAsync` перезагружает
   только при смене источника. Итог: дерево остаётся пустым, пока хост-страница вручную не вызовет
   `ReloadAsync`. Метод к тому же объявлен `async`, но не содержит `await` (предупреждение CS1998).
2. **Forced-фильтр из URL не применяется к данным.** `ApplyQueryFilter` заполняет `_filterRoot`,
   но полный режим фильтра (запрос совпадений + предки + пометки) никто не запускает.
3. **`OnParametersSetAsync`:**
   - `_source.RootId != rootId` — сравнение ссылок; для boxed-значений (`RootId = 5`) два равных
     значения считаются разными → ложные перезагрузки на каждый рендер, если страница пересоздаёт
     значение;
   - смена `OrderBy` не отслеживается вовсе;
   - при пересоздании `_source` теряются `ExtraWhere`/`ExtraWhereParams` (дефолтный фильтр
     слетает после смены любого отслеживаемого параметра).

Ключевая идея исправления: `ApplyFilterAsync` уже умеет все три режима (нет фильтра → обычная
загрузка; только дефолтный → ExtraWhere + обычная; пользовательский/forced → полный фильтр).
Поэтому и начальная загрузка, и перезагрузка при смене параметров должны идти через него.

Промт выполняется ПОСЛЕ TA2.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.razor.cs`

**1.1.** В конец `OnInitializedAsync` (после блока `if (_isDefaultOnly) { ... }` из TA2) добавить:

```csharp
// Начальная загрузка: ApplyFilterAsync сам выбирает режим
// (нет фильтра / дефолтный ExtraWhere / полный фильтр из URL).
await ApplyFilterAsync();
```

Блок `if (_isDefaultOnly) { var (defWhere, defParams) = BuildDefaultWhere(); UpdateSourceExtraWhere(...); }`
при этом УДАЛИТЬ: `ApplyFilterAsync` в дефолтном режиме делает то же самое сам. Предупреждение
CS1998 исчезнет за счёт нового `await`.

**1.2.** В `OnParametersSetAsync`:

- заменить условие
  ```csharp
  if (_source is not null &&
      (_source.SelectSql != selectSql || _source.Mode != mode || _source.RootId != rootId
       || _resolvedCsName != csName))
  ```
  на
  ```csharp
  var orderBy = Options.OrderBy;
  if (_source is not null &&
      (_source.SelectSql != selectSql || _source.Mode != mode || !Equals(_source.RootId, rootId)
       || _source.OrderBy != orderBy || _resolvedCsName != csName))
  ```
- внутри ветки заменить
  ```csharp
  _source = new ClayTreeSource(selectSql, mode, Options.Schema, Options.OrderBy, rootId);
  _dataSource = DataSource ?? new ClaySqlTreeDataSource(ResolveDb(), _source);
  await LoadRootsAsync();
  ```
  на
  ```csharp
  _source = new ClayTreeSource(selectSql, mode, Options.Schema, orderBy, rootId,
      ExtraWhere: _source.ExtraWhere, ExtraWhereParams: _source.ExtraWhereParams);
  _dataSource = DataSource ?? new ClaySqlTreeDataSource(ResolveDb(), _source);
  await ApplyFilterAsync();
  ```

**1.3.** Проверить видимость `ApplyFilterAsync`: он объявлен `private` в partial-классе
`ClayTreeView.Filter.cs` — из `ClayTreeView.razor.cs` (тот же класс) он доступен, менять
модификатор не нужно.

### Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Filter.cs`

Подстраховка от двойной загрузки при инициализации: в начало `ApplyFilterAsync` ничего добавлять
не нужно, но проверить, что в пустом режиме (`_filterRoot.Nodes.Count == 0`) вызов
`UpdateSourceExtraWhere(null, null)` стоит ДО `LoadRootsAsync()` (после TA2 так и есть) —
иначе первый рендер после сброса фильтра прошёл бы со старым `ExtraWhere`.

## Критерии приёмки

- Страница с `<ClayTreeView Options="..."/>` без каких-либо ручных вызовов показывает корни
  после первого рендера.
- URL `...?dep=eq~5` (при настроенном `UrlKey="dep"`) при открытии страницы сразу даёт режим
  фильтра: счётчик совпадений на панели, пометки, кнопка «Удалить фильтр».
- `FilterDefaults` без URL-фильтра: дерево грузится лениво с тихим WHERE (без пометок и счётчика).
- Повторные рендеры страницы с `Options.RootId = 5` (boxed int, пересоздаваемый) НЕ вызывают
  перезагрузку (проверить брейкпоинтом/логом в `ApplyFilterAsync`).
- Смена `Options.OrderBy` между рендерами вызывает перезагрузку.
