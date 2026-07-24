> Часть серии **CGO**. Перед началом прочитай **CGO0_README_grid_options.md**.
> Требует выполненного **B1**. Делай ТОЛЬКО этот шаг.

# B2 — статическая страница на `ClayGridOptions`

Первая страница, переезжающая на новый способ настройки. Здесь же складывается **эталон
разметки**, который потом попадёт в документацию (C2) и будет копироваться во все новые
страницы решения. Поэтому важен не только результат, но и то, как он выглядит.

## Прочитать

- `src/Clayzor.App.Web.MedicalTests/Components/Pages/MedicalTests.razor` — целиком, включая
  `@code`;
- `Components/Grid/ClayGridOptions.cs`;
- `Components/Grid/ClayGridPageBase.cs` — что страница получает от базового класса
  (`FilterColumnTypes`, `_rows`, `_loading`, `_query`, `OpenAddDialog`, `ToggleGroup`);
- `docs/adding-new-entity.md`, раздел «Страница» — шаблон, который в C2 придётся переписать
  ровно по итогам этого шага.

## Что сделать

**1. Поле настроек в `@code`.** Создаётся один раз, в `OnInitialized` — не выражением
в разметке (CGO0, «Остаются `[Parameter]`»), и не инициализатором поля, если для него нужны
инжектированные сервисы (`AppSettings.DefaultPageSize`): на момент инициализации полей DI ещё
не отработал, будет `NullReferenceException`.

```razor
@code {
    private ClayGrid<IClayGridRow> _dataGrid = null!;
    protected override IClayGrid? Grid => _dataGrid;

    private ClayGridOptions _gridOptions = null!;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        base.OnInitialized();

        _gridOptions = new ClayGridOptions
        {
            Title              = "…",                            // как было в разметке
            SelectSql          = SQLQueries.SELECT_…,
            SearchColumns      = [ … ],
            DefaultOrder       = "…",
            EditDialogType     = typeof(…),
            PageSize           = AppSettings.DefaultPageSize,
            FilterColumnTypes  = FilterColumnTypes,
            ShowPagination     = true,
            SelectVisible      = true,
            ShowPrint          = true,
            ShowExcel          = true,
            // …остальные, которые страница задавала явно
        };
    }
}
```

**Важно про `base.OnInitialized()`:** проверь по коду `ClayGridPageBase`, есть ли у него
`OnInitialized`/`OnInitializedAsync` и что он там делает. Если базовый класс что-то
инициализирует — вызов базовой реализации обязателен, и порядок (до сборки опций или после)
определяется тем, откуда берётся `FilterColumnTypes`. **Если `FilterColumnTypes` вычисляется
лениво при первом обращении — проверь это по коду, а не по названию.** Ошибка здесь даёт
пустой словарь типов колонок → все фильтры станут текстовыми, и это не заметно на глаз.

**2. Разметка.** Остаются только STAY-параметры:

```razor
<ClayGrid TEntity="IClayGridRow"
          @ref="_dataGrid"
          Options="_gridOptions"
          DataLoader="this"
          Items="_rows"
          Loading="_loading"
          TotalCount="@_query.TotalCount"
          PageNumber="@_query.PageNumber"
          OnAdd="OpenAddDialog"
          OnGroupToggle="ToggleGroup">

    <ColumnDefs> … без изменений … </ColumnDefs>
    <Columns>    … без изменений … </Columns>

</ClayGrid>
```

Порядок атрибутов: `TEntity`, `@ref`, `Options`, `DataLoader`, затем данные, затем колбэки.
Этот порядок — часть эталона, соблюдай его.

**3. Сверь один к одному.** Для каждого параметра, который был в разметке до правки, убедись,
что он либо в `_gridOptions` с тем же значением, либо осознанно остался атрибутом (STAY).
Параметров, задававшихся значением по умолчанию (`ShowPagination="true"` при дефолте `true`),
это тоже касается: **перенеси как есть**, не «оптимизируй» удалением. Иначе диф перестаёт
быть проверяемым, а следующая правка дефолта в библиотеке молча изменит поведение страницы.

## Не делай

- **Не собирай `ClayGridOptions` в разметке** (`Options="@(new ClayGridOptions{…})"`) — новый
  объект на каждый рендер. Это прямой запрет серии, проверяется grep'ом в CGO0.
- Не выноси `_gridOptions` в статическое поле или в `ClayGridPageBase` — у страницы своя
  конфигурация, у базового класса её быть не должно.
- Не меняй `<ColumnDefs>`, `<Columns>`, `CellTemplate`, диалог редактирования, SQL-константы.
- Не меняй набор задаваемых настроек: ни одной новой, ни одной убранной. Захотелось включить
  `EnableValueFilter` или сменить `ColumnMenuMode` — не в этом шаге.
- Не трогай `Home.razor` (B3) и второй грид, если он на этой странице есть.
- Не удаляй legacy-параметры из `ClayGrid` (C1) — после этого шага страница на `Options`,
  а параметры всё ещё существуют, и это нормальное промежуточное состояние.

## Проверка

- `dotnet build` + `dotnet test` — зелёные;
- `grep -n "SelectSql\|SearchColumns\|DefaultOrder\|EditDialogType\|ShowPrint\|ShowExcel\|SelectVisible\|PageSize\|Title" src/Clayzor.App.Web.MedicalTests/Components/Pages/MedicalTests.razor`
  → попадания только внутри `_gridOptions` в `@code`, ни одного в атрибутах `<ClayGrid>`;
- в разметке `<ClayGrid>` ровно 10 атрибутов (`TEntity`, `@ref`, `Options`, `DataLoader`,
  `Items`, `Loading`, `TotalCount`, `PageNumber`, `OnAdd`, `OnGroupToggle`) — плюс те STAY,
  которые страница задавала и раньше;
- **полный ручной чек-лист «Статический режим» из CGO0** — целиком, это главная приёмка шага.
  Особое внимание:
  - типы колонок в диалоге фильтра: у числовой колонки числовые операторы, у даты — календарь,
    у булевой — «Да/Нет» (проверка, что `FilterColumnTypes` не потерялся);
  - размер страницы в пагинаторе совпадает с `AppSettings.DefaultPageSize`;
  - кнопка «Выбрать записи», «Печать», «Выгрузка в Excel» на месте (флаги перенесены);
  - заголовок грида и текст снекбара после сохранения — как раньше;
- динамический стенд (`/?id=140`) — открыть и убедиться, что он не сломался (страница не
  тронута, но грид общий).
