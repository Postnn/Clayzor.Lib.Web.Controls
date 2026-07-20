> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненных **GF1**, **GF2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF15 — не показывать печать и Excel в динамическом режиме

Прочитать перед началом: `Components/Grid/ClayGrid.ExportMenu.cs` — целиком (все точки входа);
`Components/Grid/ClayGrid.razor` — меню групповых операций (`ShowPrint`, `ShowExcel`,
`CustomBatchGroups`, `SelectVisible`); `Components/Grid/ClayGridPageBase.cs` —
`IClayGridDataLoader` (полная поверхность интерфейса); `Kesco.App.Web.Inventory/Components/Pages/Home.razor`
— `ShowPrint="true"`, `ShowExcel="true"`.

## Дефект

**Этот дефект найден при разборе GF7, в исходном бэклоге его не было.**

Все шесть точек входа печати и Excel начинаются одинаково:

```csharp
private async Task PrintCurrentPageInternal()
{
    if (DataLoader is null) return;
    ...
}
```

`PrintCurrentPageInternal`, `PrintAllInternal`, `PrintSelectedInternal`, `ExcelCurrentPageInternal`,
`ExcelAllInternal`, `ExcelSelectedInternal` — везде `if (DataLoader is null) return;`. В динамическом
режиме `DataLoader` всегда `null`: `Home.razor` передаёт только `Dynamic="true"`, а
`NotifyQueryChanged` для динамики уходит в `LoadDynamicData` в обход `IClayGridDataLoader`.

При этом `Home.razor` ставит `ShowPrint="true" ShowExcel="true"`. Меню групповых операций
рисуется, подменю «Печать» и «Выгрузка в Excel» раскрываются, пункты кликаются — и молча
не делают ничего. Ни спиннера, ни ошибки, ни снекбара. Худший вид поломки: пользователь
считает, что не сработало нажатие, и жмёт ещё раз.

Реализация печати и Excel для динамического режима — это отдельная фича: нужен
`IClayGridDataLoader` поверх `DynamicSql` (загрузка всех строк без пагинации, выборка по
списку ID, `BuildAllRowsForSelected`, группировка для `BuildPrintHtmlAsync`). Часть его
методов упирается в группировку, которой в динамическом режиме нет (GF14). В серию багфиксов
это не входит — здесь убираем нерабочий UI.

Заодно зафиксируй в `GF7_backlog.md` (пункт про экспорт), что фильтр по значению (Excel-style)
в динамическом режиме тоже недоступен, но иначе: `InitDynamicMode` не выставляет
`AllowValueFilter` в `ClayColumnMeta` (по умолчанию `false`), поэтому
`IsValueFilterAvailable` возвращает `false`, значок в заголовке не рисуется и до
`DataLoader!.LoadDistinctValuesAsync` (с `!`) дело не доходит. То есть NRE там нет — но
если кто-то поставит `AllowValueFilter = true`, будет `NullReferenceException`. Отдельного
фикса сейчас не требует, но в бэклог занеси.

## Изменить/создать

**1.** `ClayGrid.razor` — не показывать подменю, которые не работают. В меню групповых
операций заменить условия:

```razor
@if (ShowPrint && DataLoader is not null)
{
    ... подменю «Печать» ...
}
@if (ShowExcel && DataLoader is not null)
{
    ... подменю «Выгрузка в Excel» ...
}
```

`CustomBatchGroups` не трогай — их обработчики (`BatchOperation.OnExecute`) реализуются в
приложении и от `DataLoader` не зависят.

**2.** Если после этого меню групповых операций (`ClayMenu` с иконкой `PlaylistAddCheck`)
остаётся пустым — то есть `ShowPrint`/`ShowExcel` отключены или `DataLoader is null`, и
`CustomBatchGroups` не заданы — не показывать и его. Условие вынеси в `ClayGrid.razor.cs`
отдельным свойством, а не собирай выражение в разметке:

```csharp
/// <summary>
/// Есть ли что показать в меню групповых операций. Печать и Excel работают только
/// через IClayGridDataLoader; в динамическом режиме его нет (см. GF15).
/// </summary>
private bool _hasBatchOperations =>
    (ShowPrint && DataLoader is not null)
    || (ShowExcel && DataLoader is not null)
    || (CustomBatchGroups?.Count > 0);
```

**3.** `Home.razor` — убрать `ShowPrint="true"` и `ShowExcel="true"`: они ничего не включают
и вводят в заблуждение читающего разметку.

**4.** Xml-doc параметров `ShowPrint` и `ShowExcel` в `ClayGrid.razor.cs` — дописать одной
строкой: работает только при заданном `DataLoader` (статический режим).

## Не делай

Не реализовывай `IClayGridDataLoader` для динамического режима — это отдельный план.
Не убирай `if (DataLoader is null) return;` из `ClayGrid.ExportMenu.cs` — это защита, она
остаётся. Не трогай `ClayGridExcelGenerator`, `ClayGridPrintHtmlGenerator`, JS-модули печати
и Excel. Не трогай `SelectVisible` и режим выбора — это GF13. Не трогай статический режим:
там `DataLoader="this"` передаётся всегда, условия истинны, поведение прежнее.

## Проверка (ручная)

- `?id=140` → кнопка меню групповых операций не показывается вовсе (при `SelectVisible="true"`
  и без `CustomBatchGroups`);
- временно передать в `Home.razor` `SelectVisible="true"` и один `CustomBatchGroups` →
  меню появилось, в нём ТОЛЬКО кастомная группа, подменю «Печать» и «Выгрузка в Excel» нет,
  кастомная операция выполняется;
- статический режим (`MedicalTests.razor`): меню на месте, «Печать → Текущая страница»,
  «Печать → Все данные», «Excel → Текущая страница», «Excel → Все данные» работают;
  с выбранными строками появляются пункты «Выбранные (N)» и тоже работают — регрессии нет;
- `GF7_backlog.md` дополнен: экспорт/печать в динамическом режиме, фильтр по значению и
  `DataLoader!` в `OpenValueFilterDialog`.
