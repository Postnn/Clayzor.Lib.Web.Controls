# F2. Панель грида: текст составного фильтра ИЛИ чипы колонок (взаимоисключающе)

Файлы: `Components/Grid/ClayGrid.razor` (блок трея фильтра),
при необходимости `Components/Grid/ClayGrid.Filtering.cs`.
Закрывает баг 4.

## Проблема
Сейчас трей рисует **одновременно**: чипы по каждому листу `ColumnDialog`
(цикл `ColumnDialogLeaves`, безусловный) и отдельный чип составного фильтра
(`_hasComposite`). При наличии и того, и другого панель выглядит криво.

## Требуемое поведение
- Если в дереве есть **хотя бы один узел составного фильтра** (`_hasComposite` —
  уже вычисляется: любой узел, кроме листа `Source=ColumnDialog`) →
  показывать **только текст всего фильтра** (единый блок сегментов по всему дереву).
- Иначе → показывать **чипы колонок** (текущее поведение с `ColumnDialogLeaves`).

## Реализация (ClayGrid.razor, блок `@if (_filterTrayExpanded)`)
Развести на две взаимоисключающие ветки:

```razor
@if (_hasComposite)
{
    @* Единый текст всего дерева; каждое условие — кликабельный сегмент,
       маршрут по происхождению *@
    <div class="filter-chip filter-chip--composite">
        @foreach (var seg in _filterSegments)
        {
            var s = seg;
            <span class="chip-label chip-label-clickable"
                  @onclick="@(() => RouteSegmentClick(s))"
                  @onclick:stopPropagation="true">@s.Text</span>
        }
        <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                       OnClick="@ClearAllFilters" Class="chip-remove-btn pa-0"
                       Style="width:18px;height:18px" title="Очистить фильтр" />
    </div>
}
else
{
    @* существующие чипы по ColumnDialogLeaves — как сейчас *@
}
```

## Хелперы (ClayGrid.Filtering.cs)
```csharp
// Маршрутизация клика по сегменту: колоночный → диалог колонки; иначе → составной.
private Task RouteSegmentClick(FilterSegment seg)
{
    if (seg.Source == ClayFilterSource.ColumnDialog)
    {
        var dn = _columnBySqlName.TryGetValue(seg.Column, out var m) ? m.DisplayName : seg.Column;
        return OpenFilterDialog(seg.Column, dn);
    }
    return OpenCompositeFilterDialog();
}

private async Task ClearAllFilters()
{
    _filterRoot = new();
    _pageNumber = 1;
    await NotifyQueryChanged();
}
```

Примечание: текст в режиме `_hasComposite` представляет **весь** фильтр (включая
колоночные листья), с сохранением маршрутизации редактирования по сегментам.
`ClearAllFilters` очищает всё дерево (единая кнопка очистки для единого текста).

## Критерии
- [ ] Есть составные условия → панель показывает только текст всего фильтра; чипов колонок нет.
- [ ] Нет составных условий → панель показывает чипы колонок (как раньше).
- [ ] Клик по сегменту открывает нужный диалог; очистка сбрасывает фильтр и перезагружает данные.
- [ ] `dotnet build` без ошибок.

## Отдельный вопрос (решить и, если нужно, поправить)
`ToggleFilterTray` при **сворачивании** трея сейчас обнуляет весь фильтр
(`_filterRoot = new()` в ветке `!_filterTrayExpanded`). Сворачивание панели теряет
настроенный фильтр — вероятно, нежелательно. Если так — убрать сброс из
`ToggleFilterTray` (оставить сброс только за явной кнопкой очистки / «Сбросить»).

---

## Drop-семантика перетаскивания колонки (вариант A) — обязательно

Перетаскивание колонки на панель работает одинаково независимо от того, есть ли
составные условия (обработчик `@ondrop="OnFilterTrayDrop"` на внешнем `<div class="filter-tray">`
не меняется — F2 правит только внутреннее содержимое). Зафиксировать поведение:

- Брошенная колонка → `OpenFilterDialog(sqlName)` → диалог колонки → на «ОК» лист
  `ColumnFilter` с `Source=ColumnDialog` вставляется/заменяется **на верхнем уровне**
  (`_filterRoot.Nodes`) и объединяется с остальными узлами через **текущую логику
  корня** (`_filterRoot.Logic`). Отдельного «И-сужения» не навязываем.
- При `_hasComposite == true` новый лист не рисуется отдельным чипом, а появляется
  **сегментом в едином тексте** дерева (его отдаёт `BuildFilterSegments`, обходя всё
  дерево). Клик по такому сегменту через `RouteSegmentClick` открывает диалог колонки
  (по `Source=ColumnDialog`).

### Известные следствия варианта A (задокументировать, не «чинить»)
1. **Объединение по логике корня.** Если пользователь переключил корень на `ИЛИ`,
   брошенная колонка окажется под `ИЛИ` (расширит, а не сузит выборку). Это ожидаемое
   поведение варианта A. Опционально: при наличии колоночных листьев показывать рядом
   с переключателем корня короткую подсказку («логика верхнего уровня применяется и к
   перетащенным колонкам»). Реструктуризацию дерева (вариант B) НЕ делаем.
2. **Поиск замены — только на верхнем уровне.** `OpenFilterDialog` ищет существующий
   колоночный лист по `Column` лишь в `_filterRoot.Nodes` (без захода во вложенные
   группы). Если по той же колонке уже есть условие внутри группы — drop добавит второй,
   корневой лист. Это допустимо; менять поиск не требуется.

### Индивидуальное удаление колоночного условия в составном режиме (включить)
Чтобы в режиме `_hasComposite` можно было убрать отдельное перетащенное условие
(а не только «очистить всё»), у сегментов с `Source=ColumnDialog` добавить маленький
крестик:
```razor
@foreach (var seg in _filterSegments)
{
    var s = seg;
    <span class="chip-label chip-label-clickable"
          @onclick="@(() => RouteSegmentClick(s))" @onclick:stopPropagation="true">@s.Text</span>
    @if (s.Source == ClayFilterSource.ColumnDialog)
    {
        <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                       OnClick="@(() => RemoveFilter(s.Column))"
                       Class="chip-remove-btn pa-0" Style="width:16px;height:16px" />
    }
}
```
`RemoveFilter(sqlName)` уже есть в `ClayGrid.Filtering.cs`. Составные условия
(`Source=CompositeDialog`) индивидуального крестика не получают — их правят через
диалог настраиваемого фильтра; для полного сброса остаётся общая «Очистить фильтр».

### Дополнить критерии
- [ ] Drop колонки при активном составном фильтре добавляет условие на верхний уровень
      по логике корня и показывает его сегментом в общем тексте.
- [ ] Клик по колоночному сегменту открывает диалог колонки; по составному — диалог фильтра.
- [ ] У колоночных сегментов есть индивидуальное удаление; у составных — нет.
