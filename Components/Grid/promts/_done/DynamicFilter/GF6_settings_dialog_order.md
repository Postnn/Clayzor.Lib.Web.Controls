> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненного **GF4**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF6 — источник порядка в диалоге настройки колонок — только `_columnOrder`

Прочитать перед началом: `Components/Grid/ClayGrid.razor.cs` — `BuildColumnSettingsItems`,
`OpenColumnSettings`, `_columnOrder`, `_columnById`, `_columnBySqlName`, `_hiddenSqlNames`,
`_valueFilterDisabledColumns`; `Components/Grid/ClayColumnSettingsDialog.razor` — что делает
диалог с переданным списком; `Components/Grid/ClayGridPageBase.Export.Print.cs` и
`ClayGridPageBase.Export.Excel.cs` — там `BuildColumnSettingsItems` переиспользуется для
подготовки колонок к печати/экспорту.

## Дефект

```csharp
var items = _columnBySqlName.Values
    .OrderBy(m => _columnOrder.IndexOf(m.ColumnId))
    .Select(m => new ColumnSettingsItem { ... })
    .ToList();
```

Источник списка — `_columnBySqlName`, источник порядка — `_columnOrder`. Это разные множества.
В динамическом режиме в `_columnBySqlName` дополнительно лежат фильтр-онли колонки (Тип 6
`ConditionBool`, Тип 11 `ConditionList`): они регистрируются в `InitDynamicMode` отдельным
блоком и в `_columnOrder` сознательно не добавляются. Для них `_columnOrder.IndexOf(...)`
возвращает `−1`, то есть:

- в диалоге «Настройка колонок» они всплывают выше всех настоящих колонок;
- `OpenColumnSettings` по «ОК» делает `_columnOrder.Clear()` и заполняет его из
  `updatedItems` — фильтр-онли колонки попадают в `_columnOrder` и ломают порядок;
- дальше `SerializeColumns` пишет их в `cols+gridId`, и мусор закрепляется в БД.

`_columnBySqlName` — словарь, порядок его `Values` вообще не определён; опираться на него
как на источник списка колонок вывода нельзя. Единственный источник порядка — `_columnOrder`.

В статическом режиме дефект не проявляется: там каждая зарегистрированная колонка проходит
через `RegisterColumnInOrder`, множества совпадают. Поведение статического режима после фикса
не меняется.

## Изменить/создать

`ClayGrid.razor.cs`, `BuildColumnSettingsItems()` — строить список из `_columnOrder`:

```csharp
var items = _columnOrder
    .Select(id => _columnById.GetValueOrDefault(id))
    .Where(m => m is not null)
    .Select(m => new ColumnSettingsItem
    {
        SqlName           = m!.SqlName,
        DisplayName       = m.DisplayName,
        IsVisible         = !_hiddenSqlNames.Contains(m.SqlName) && !IsGrouped(m.SqlName),
        IsReadonly        = IsGrouped(m.SqlName),
        AllowValueFilter  = !_valueFilterDisabledColumns.Contains(m.SqlName) && m.AllowValueFilter,
    })
    .ToList();
```

`OrderBy(...IndexOf...)` убирается — `_columnOrder` уже упорядочен. Блок досбора `SortPriority`
/ `IsSortDesc` из `_sortState` ниже по методу оставить без изменений.

## Не делай

Не меняй `OpenColumnSettings` — после фикса `updatedItems` содержит ровно колонки вывода, и
`_columnOrder.Clear()` + пересборка становятся корректными. Не меняй `ClayColumnSettingsDialog`.
Не добавляй фильтр-онли колонки в `_columnOrder`. Не трогай `IClayGrid.GetVisibleColumns()` —
он уже строится от `_columnOrder`.

## Проверка (ручная)

- `?id=140&CLID=9` → «Настройка колонок»: список ровно из колонок вывода, в том же порядке,
  что в гриде; колонок Тип 6/11 в списке нет;
- переставить две колонки, «ОК» → порядок в гриде совпадает с диалогом; открыть диалог снова →
  порядок тот же (не «уехал»);
- «Отмена» → порядок откатился на снимок `_columnOrderSnapshot`;
- сгруппировать колонку → в диалоге она `IsReadonly`, чекбокс недоступен;
- печать и выгрузка в Excel («Текущая страница») дают колонки в том же порядке, что в гриде —
  `BuildColumnSettingsItems` переиспользуется там же;
- статический режим (`MedicalTests.razor`): диалог настройки колонок работает как раньше,
  список и порядок не изменились.
