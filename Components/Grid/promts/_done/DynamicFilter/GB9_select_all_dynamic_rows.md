> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **GF13_dynamic_row_selection.md**. **Блокирует приёмку GB1.** Требует выполненного **GB8** (иначе проверка упрётся в MARS-исключение). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB9 — «выделить всё» не выделяет строки динамического грида

Прочитать перед началом: `Components/Grid/ClayGrid.Grouping.cs` — `OnHeaderTriToggle` целиком;
`Components/Grid/ClayGrid.Selection.cs` — `IsHeaderIndeterminate`, `ComputeSelectAllState`,
`OnRowSelectAsync` (как там сделано ПОСЛЕ GF13); `Components/Grid/ClayGrid.Dynamic.cs` —
`TryGetSelectionId`; `Components/Grid/ClayGridRow.cs` — `IDetailRow`, `DetailRow<T> where T : Entity`;
`Components/Grid/Dynamic/ClayDynamicRow.cs`; `GF13_dynamic_row_selection.md` — раздел «Изменить/создать»,
пункт 3 (список методов, которые GF13 обязан был перевести на `TryGetSelectionId`).

## Дефект

**GF13 пропустил один метод.** Он перевёл на `TryGetSelectionId` разметку колонки выбора,
`IsHeaderIndeterminate` и `ComputeSelectAllState`, но `OnHeaderTriToggle` — обработчик клика
по чекбоксу «выделить всё» в шапке — остался на старой проверке
(`ClayGrid.Grouping.cs`, ~строка 201):

```csharp
foreach (var row in Items ?? [])
{
    if (row is IDetailRow dr && dr.Item is Entity entity)      // ← мёртвая ветка в динамике
    {
        if (!anySelected) _selectedIds.Add(entity.Id);
        else _selectedIds.Remove(entity.Id);
    }
    else if (row is GroupHeaderRow gh) { … }                   // ← а эта работает
}
```

`ClayDynamicRow` — не `Entity` и им быть не может (GF13, раздел «Дефект»), поэтому
`dr.Item is Entity entity` в динамическом режиме всегда `false`.

Как это выглядит для пользователя после GB1:

- **плоский динамический грид**: клик по «выделить всё» → не выделяется НИЧЕГО, чекбокс шапки
  при этом переключился (`_selectAllChecked = !anySelected` отработал до цикла) → шапка
  «отмечена», строки — нет;
- **группированный динамический грид**: выделятся только потомки групп (через
  `GetChildIdsForGroup`), а детальные строки раскрытых групп на странице — нет. Часть записей
  «выделится», часть нет, без видимой логики;
- **снять выделение** тем же чекбоксом — та же половинчатость;
- статический режим не затронут: `DetailRow<T> where T : Entity`, ветка живая.

`grep -rn "is Entity" Components/Grid` после GF13 обязан был давать одно попадание —
в `TryGetSelectionId` (`ClayGrid.Dynamic.cs`). Даёт два.

## Изменить/создать

`Components/Grid/ClayGrid.Grouping.cs`, `OnHeaderTriToggle` — привести к тому же виду, что
`ComputeSelectAllState` и `IsHeaderIndeterminate` после GF13:

```csharp
foreach (var row in Items ?? [])
{
    if (row is IDetailRow dr && TryGetSelectionId(dr.Item, out var eid))
    {
        if (!anySelected) _selectedIds.Add(eid);
        else _selectedIds.Remove(eid);
    }
    else if (row is GroupHeaderRow gh)
    {
        /* … как было … */
    }
}
```

Изменение ровно одно: `dr.Item is Entity entity` → `TryGetSelectionId(dr.Item, out var eid)`,
`entity.Id` → `eid`. Логика (`anySelected`, догрузка `missingKeys` через
`LoadChildIdsForGroupsAsync`, порядок веток, `StateHasChanged`) — без изменений.

Если после правки `using Clayzor.Lib.Entities` в `ClayGrid.Grouping.cs` осиротел — убрать
(`/AGENTS.md`: чистим орфанов, созданных своими изменениями). Проверить сборкой, а не глазами:
в файле могут быть другие потребители неймспейса.

Контрольная проверка после правки: `grep -rn "is Entity" src/Clayzor.Lib.Web.Controls/Components/Grid`
→ ровно одно попадание, `TryGetSelectionId` в `ClayGrid.Dynamic.cs`.

## Не делай

- Не трогай `OnGroupTriToggle`, `OnGroupSelectAsync`, `OnRowSelectAsync`,
  `ComputeGroupCheckState` — они работают через `GetChildIdsForGroup`/`TryGetSelectionId`
  и уже корректны.
- Не меняй `TryGetSelectionId` и тип `_selectedIds`.
- Не «улучшай» семантику «выделить всё» (сейчас — только текущая страница, `Items`): это
  осознанное поведение, общее для обоих режимов.
- Не выноси общий цикл обхода `Items` из `OnHeaderTriToggle`/`ComputeSelectAllState`/
  `IsHeaderIndeterminate` в общий хелпер — рефакторинг, не багфикс.

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`, **без группировки**, «Выбрать записи» ВКЛ →
  «выделить всё» в шапке → отмечены ВСЕ строки страницы, в меню групповых операций
  «Выбранные (N)», где N = число строк на странице; повторный клик → снято всё;
- отметить одну строку из десяти → шапка indeterminate; клик по шапке → выделены все;
  клик ещё раз → снято всё;
- **с группировкой**, одна группа раскрыта → «выделить всё» → выделены и детальные строки
  раскрытой группы, и все потомки свёрнутых групп; счётчик «Выбранные (N)» совпадает с суммой
  счётчиков групп на странице;
- выделить всё → перейти на страницу 2 → выделить всё → вернуться на страницу 1 → отметки
  первой страницы на месте (`_selectedIds` персистентен);
- выделить всё → «Выгрузка в Excel» → «Выбранные (N)» → в файле ровно N строк;
- сменить сортировку → выбор сброшен;
- `/medical-tests` (статика): «выделить всё» работает как до фикса — на плоском гриде и на
  группированном;
- `grep -rn "is Entity" Components/Grid` → одно попадание;
- `dotnet build` + `dotnet test` — зелёные.
