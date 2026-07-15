> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненных **GF1**, **GF2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF13 — чекбоксы выбора строк в динамическом режиме

Прочитать перед началом: `Components/Grid/ClayGrid.Selection.cs` — целиком (`_selectedIds`,
`IsHeaderIndeterminate`, `ComputeSelectAllState`, `OnRowSelectAsync`); `Components/Grid/ClayGrid.razor`
— колонка выбора (`@key="@SelectColumnKey"`), `CellTemplate` и `HeaderTemplate`;
`Components/Grid/ClayGrid.Dynamic.cs` — `GetRowIdValue`, `_dynamicDef.IdColumn`;
`Components/Grid/Dynamic/ClayDynamicRow.cs` (создан в GF1); `Components/Grid/ClayGridRow.cs` —
`DetailRow<T> where T : Entity`; `Clayzor.Lib.Entities/Entity.cs` — свойство `Id`.

## Дефект

Разметка колонки выбора:

```razor
else if (context.Item is IDetailRow dr)
{
    var ent = dr.Item as Entity;
    if (ent != null)
    {
        var eid = ent.Id;
        ...
    }
}
```

`ClayDynamicRow` (GF1) — не `Entity` (и не может им быть: `Entity` — базовый класс статических
сущностей, у динамической строки нет типа). `as Entity` даёт `null` → чекбокса у строки нет.
При `SelectVisible="true"` режим выбора в динамическом гриде включается, но выбирать нечего:
`_selectedIds` всегда пуст, «Выбранные (N)» в меню групповых операций не появляется.

Ровно та же проверка `dr.Item is Entity e` стоит в `IsHeaderIndeterminate` и
`ComputeSelectAllState` — чекбокс «выделить всё» тоже мёртв.

ID строки в динамическом режиме берётся не из `Entity.Id`, а из колонки `_dynamicDef.IdColumn`
(имя приходит из `Запросы.ID`). Механизм уже есть — `GetRowIdValue(object? rowItem)`, он
возвращает `string?`.

## Изменить/создать

`_selectedIds` — `HashSet<int>`, и на `int` завязаны `_groupChildIds`, `ExcelExportRequest.SelectedIds`,
`ClayGridPageBase.Export.Excel.BuildAllRowsForSelected`. Менять тип ключа на `string` — большой
рефакторинг статического режима, в этот шаг не входит. Динамический режим приводит ID к `int`.

**1.** `ClayGrid.Dynamic.cs` — единая точка получения ID для выбора:

```csharp
/// <summary>
/// ID строки для режима выбора. В динамическом режиме берётся из колонки
/// <c>_dynamicDef.IdColumn</c>, в статическом — из <see cref="Entity.Id"/>.
/// Возвращает false, если ID нечисловой: выбор для такого грида недоступен.
/// </summary>
private bool TryGetSelectionId(object? rowItem, out int id)
{
    id = 0;

    if (Dynamic)
    {
        var raw = GetRowIdValue(rowItem);
        return raw is not null && int.TryParse(raw, out id);
    }

    if (rowItem is Entity e)
    {
        id = e.Id;
        return true;
    }

    return false;
}
```

**2.** `ClayGrid.razor`, `CellTemplate` колонки выбора:

```razor
else if (context.Item is IDetailRow dr && TryGetSelectionId(dr.Item, out var eid))
{
    var detailChecked = _selectedIds.Contains(eid);
    <ClayCheckbox State="(bool?)detailChecked"
                   Title="Выбрать запись"
                   OnToggle="() => OnRowSelectAsync(eid, !detailChecked)" />
}
```

**3.** `ClayGrid.Selection.cs` — заменить `dr.Item is Entity e` → `TryGetSelectionId(dr.Item, out var eid)`
в `IsHeaderIndeterminate` и `ComputeSelectAllState`, `e.Id` → `eid`. Логика не меняется.

**4.** `TryGetSelectionId` живёт в `ClayGrid.Dynamic.cs`, но используется из `Selection.cs` и
разметки — это один partial-класс, дополнительных объявлений не нужно. `Entity` уже
подключён через `using Clayzor.Lib.Entities` в `Selection.cs`.

## Не делай

Не меняй тип `_selectedIds` / `_groupChildIds` / `ExcelExportRequest.SelectedIds` с `int` на
`string`. Не трогай ветку `context.Item is GroupHeaderRow` в колонке выбора — групповые
чекбоксы идут через `GetChildIdsForGroup`/`LoadChildIdsForGroupsAsync`, а те работают через
`DataLoader`, которого в динамическом режиме нет; группировка динамического грида — GF14.
Не трогай `Home.razor` (там `SelectVisible` не задан — включи вручную для проверки).
Печать/Excel выбранных в динамическом режиме — GF15, здесь не трогай.

## Проверка (ручная)

- временно добавить в `Home.razor` `SelectVisible="true"`, открыть `?id=140`;
- «Выбрать записи» → у строк появились чекбоксы; отметить две → в меню групповых операций
  видно «Выбранные (2)»;
- чекбокс «выделить всё» в шапке: отметить одну строку из десяти → шапка в состоянии
  indeterminate; отметить все → шапка `checked`; снять → `unchecked`;
- перейти на страницу 2, отметить строку, вернуться на страницу 1 → отметки первой страницы
  сохранились (`_selectedIds` персистентен между страницами);
- сменить сортировку → выбор сброшен (`NotifyQueryChanged`, ветка `essenceChanged`);
- статический режим (`MedicalTests.razor`, `SelectVisible="true"`): выбор строк, «выделить всё»
  и групповые операции работают ровно как до фикса;
- негативный: временно поставить в `Запросы.ID` для грида 140 имя нечисловой колонки
  (например, `Название`) → чекбоксов строк нет, грид не падает, ошибок в консоли нет.
  Вернуть `КодИсследования` обратно.
