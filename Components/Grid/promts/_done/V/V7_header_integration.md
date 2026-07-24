# V7. Интеграция в заголовок: значок слева, подсветка, маршрутизация

Собрать фичу воедино: значок фильтра по значению в **левом углу** заголовка
колонки (треб. 11), подсветка при активном фильтре (треб. 13), открытие диалога
V6 с ленивой загрузкой V4, применение результата в дерево `_filterRoot` со
взаимоисключением фильтра по условию (треб. 8, 9) и маршрутизация «открыть форму
условия» (треб. 7). Инверсию уже посчитал V6, SQL строит V2.

## Файлы
- `Components/Grid/ClayColumn.razor` — значок слева в `HeaderTemplate`.
- `Components/Grid/IClayGrid.cs` — методы доступа (иконка/состояние/открытие).
- `Components/Grid/ClayGrid.Filtering.cs` — открытие диалога V6, применение
  результата, маршрутизация в `ClayColumnFilterDialog` (с `InitialOperator`).
- `Components/Grid/ClayGrid.razor.cs` — при необходимости реализация методов
  `IClayGrid` и проброс `LoadDistinctValuesAsync` в замыкание для V6.

## ⚠️ ВАЖНО: шапку рисует ДВА разных места
`ClayGrid.razor` в установившемся состоянии рендерит заголовки **не** через
`ClayColumn.HeaderTemplate`. Две ветки в `<Columns>`:
- `@Columns` — только пока `!_columnsReady` (fallback/во время загрузки): это
  компоненты `ClayColumn` с их `HeaderTemplate`.
- Основная (`_columnsReady == true`): цикл `@foreach (var colId in _columnOrder)`
  строит **собственные** `<TemplateColumn>` с **инлайновым** `HeaderTemplate`
  прямо в `ClayGrid.razor`.
Значок нужно добавить в **ОБА** места, иначе он виден только во время загрузки и
исчезает после (симптом: безусловный маркер в шапке пропадает по окончании
загрузки). Приоритет — инлайновая шапка в `ClayGrid.razor`.

### Инлайновая шапка в `ClayGrid.razor` (обязательно)
В блоке `@foreach (var colId in _columnOrder)`, в начале `<HeaderTemplate>`,
сразу после внешнего `<div style="display:flex;align-items:center;width:100%">`
и ПЕРЕД `<div style="flex:1;...cursor:grab">` вставить:
```razor
@if (((IClayGrid)this).IsValueFilterAvailable(sqlName))
{
    var vfSql    = sqlName;
    var vfActive = ((IClayGrid)this).IsValueFilterActive(vfSql);
    <MudIconButton Icon="@Icons.Material.Filled.FilterList"
                   Size="Size.Small"
                   Color="@(vfActive ? Color.Primary : Color.Default)"
                   OnClick="async () => await ((IClayGrid)this).OpenValueFilterDialog(vfSql)"
                   Style="padding:0;width:22px;height:22px;min-width:22px;margin-right:2px"
                   title="Фильтр по значению" />
}
```
Вызовы — через `((IClayGrid)this).…` (это явные реализации интерфейса, как уже
сделано для `OpenValueFilterDialog` в существующем коде). `sqlName`/`ordMeta` —
локальные переменные цикла, уже в области видимости шаблона.

#### ⚠️ Цвет значка на тёмной шапке (иначе значок «невидим», но кликается)
Фон шапки грида тёмный (`--lh-navy`), а `.mud-table-head .mud-table-cell` имеет
`color: white !important`. `MudIconButton` с `Color.Default` рисует иконку тёмным
цветом → тёмное на тёмном: кнопка в DOM есть и кликается, но визуально не видна.
Не задавать цвет через `Color=`, а красить SVG через CSS-класс (как уже сделано
для `.clay-column-menu`). На кнопке:
`Class="@("clay-vf-icon" + (vfActive ? " clay-vf-icon--active" : ""))"`.
Добавить в `app.css` рядом с правилом `.clay-column-menu` (подсветка активного —
треб. 13):
```css
.clay-vf-icon .mud-svg-icon path:not([fill="none"]) { fill: var(--lh-white) !important; opacity: .55; }
.mud-table-cell:hover .clay-vf-icon .mud-svg-icon path:not([fill="none"]) { opacity: 1; }
.clay-vf-icon--active .mud-svg-icon path:not([fill="none"]) { fill: var(--lh-gold) !important; opacity: 1; }
```

### Значок в fallback-шапке (`ClayColumn.razor`)
В `HeaderTemplate` сейчас: слева гибкий блок с текстом+drag, справа — `ClayMenu`
(⋮). Добавить **слева** (перед гибким блоком) значок фильтра по значению:
- Видимость: только когда
  `Grid.IsValueFilterAvailable(_meta.SqlName)` == true
  (грид проверяет `EnableValueFilter && meta.Filterable && meta.AllowValueFilter`).
- Иконка: `Icons.Material.Filled.FilterList` (или существующая иконка «воронка»
  из темы). Цвет по состоянию (треб. 13):
  `Grid.IsValueFilterActive(_meta.SqlName)` → `Color.Primary`/выделенный,
  иначе `Color.Default`/приглушённый.
- Клик: `await Grid.OpenValueFilterDialog(_meta.SqlName)`.
- Разместить как `MudIconButton` малого размера (padding:0; ~22px, как активатор
  `ClayMenu`), чтобы не ломать высоту строки заголовка.

## `IClayGrid` — добавить
- `bool IsValueFilterAvailable(string sqlName);`
- `bool IsValueFilterActive(string sqlName);`  // есть `ValueFilter`-лист по колонке
- `Task OpenValueFilterDialog(string sqlName);`
Реализация в `ClayGrid.razor.cs`/`ClayGrid.Filtering.cs`.

## Открытие диалога (`ClayGrid.Filtering.cs`)
Метод `OpenValueFilterDialog(sqlName)`:
1. Найти `meta = _columnBySqlName[sqlName]`; тип —
   `FilterColumnTypes.TryGetValue(sqlName, out var t) ? t : ColumnType.Text`.
2. Найти в `_filterRoot.Nodes`:
   - `existingValue` = первый `ValueFilter` с `Column==sqlName`;
   - `existingCond`  = первый `ColumnFilter` с `Column==sqlName` и
     `Source==ColumnDialog` (как в `OpenFilterDialog`).
3. Замыкание загрузки для V6:
   `Func<Task<DistinctValuesResult>> load = () => DataLoader.LoadDistinctValuesAsync(sqlName, BuildCurrentQuery(), 100);`
   Использовать текущее состояние запроса (тот же снимок, что уходит в
   `NotifyQueryChanged`; см. как формируется `ClayDataQuery` в гриде — переиспользовать
   имеющийся сборщик снимка запроса, не изобретать новый).
4. `DialogParameters<ClayColumnValueFilterDialog>` с
   `ColumnSqlName`, `ColumnDisplayName`, `ColumnType`, `BoolTrueLabel`,
   `BoolFalseLabel` (из `meta`), `ExistingValueFilter=existingValue`,
   `ExistingConditionFilter=existingCond`, `LoadValues=load`.
   Опции — как у `OpenFilterDialog` (`DialogOptionsEx`, `DragMode=Simple`,
   `MaxWidth.ExtraSmall`).
5. По результату:
   - `ValueFilter vf` → **применить** (см. ниже);
   - `Cleared` → удалить `existingValue` из дерева (если был), перезагрузить;
   - `OpenConditionRequest(op)` → закрыть и вызвать существующий
     `OpenFilterDialog(sqlName, meta.DisplayName)`, но с пресетом оператора:
     прокинуть `InitialOperator=op` (V5) — расширить `OpenFilterDialog` необязательным
     параметром `ColumnFilterOperator? initialOperator = null` и передать его в
     `DialogParameters<ClayColumnFilterDialog>`;
   - `RemoveConditionRequest` → удалить `existingCond` (переиспользовать
     `RemoveFilter(sqlName)`), затем при желании снова открыть диалог значений.

## Применение `ValueFilter` в дерево (взаимоисключение, треб. 8, 9)
Отдельный метод `ApplyValueFilter(ValueFilter vf)`:
- В колонке одновременно **не должно** быть и условия, и значения. Перед вставкой
  `ValueFilter` удалить из `_filterRoot.Nodes` существующий `ColumnFilter`
  (`Source=ColumnDialog`, `Column==sqlName`) и старый `ValueFilter` этой колонки.
- Вставить `vf` в `_filterRoot.Nodes` (лист верхнего уровня, как делает
  `OpenFilterDialog` для `ColumnFilter`). Логика корня — как есть (`_filterRoot`
  остаётся источником истины; SQL соберёт `ClayCompositeSqlBuilder`).
- Симметрично: при применении фильтра **по условию** (`OpenFilterDialog`) — если
  для колонки уже есть `ValueFilter`, удалить его перед вставкой условия
  (добавить эту зачистку в существующий `OpenFilterDialog`).
- `_pageNumber = 1; await NotifyQueryChanged();` (как в текущих методах).

## Подсветка (треб. 13)
`IsValueFilterActive(sqlName)` = в `_filterRoot.Nodes` есть `ValueFilter` с
`Column==sqlName` и `HasValue`. Значок в заголовке подписывается на перерисовку
через уже существующее `TrayStateChanged`/`ColumnsChanged` (либо вызвать
`StateHasChanged` грида после `NotifyQueryChanged` — заголовки перерисуются).

## Критерии
- [ ] Значок добавлен в ОБА пути отрисовки шапки: инлайновый `HeaderTemplate` в
      `ClayGrid.razor` (цикл по `_columnOrder`, основной) И `ClayColumn.razor`
      (fallback). Значок не пропадает после окончания загрузки данных.
- [ ] Значок в **левом** углу заголовка, виден только для колонок с включённым
      режимом (11), меняет цвет при активном фильтре по значению (13).
- [ ] Диалог V6 открывается с ленивым `LoadValues`, использующим текущий контекст
      запроса; значения не грузятся до открытия (2).
- [ ] Результат корректно применяется/снимается; страница сбрасывается на 1;
      данные перезагружаются.
- [ ] Взаимоисключение: применение значения удаляет условие колонки и наоборот
      (8, 9).
- [ ] Клик по условию из диалога значений открывает `ClayColumnFilterDialog` с
      пресетом оператора (7, через V5).
- [ ] `dotnet build` без ошибок.
