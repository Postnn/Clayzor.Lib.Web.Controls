> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненных **GF1**, **GF2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF14 — не предлагать группировку в динамическом режиме

Прочитать перед началом: `Components/Grid/ClayGrid.Grouping.cs` — целиком; `Components/Grid/ClayGrid.Dynamic.cs`
— `LoadDynamicData`, сборка `ClayColumnMeta` в `InitDynamicMode` (`Groupable = true`),
`RestoreDynamicState` → `ApplySavedGroups`; `Components/Grid/ClayGrid.razor` — кнопка
«Группировать» (`_columnBySqlName.Values.Any(c => c.Groupable)`), лоток группировки, ветки
`context.Item is GroupHeaderRow`; `Components/Grid/ClayGridPageBase.cs` — как `GroupedPage<T>`
собирается в статическом режиме; `Components/Grid/ClayGrid.DragDrop.cs` — drop колонки в лоток.

## Дефект

`LoadDynamicData` игнорирует группировку полностью:

```csharp
var searchWhere = query.BuildWhereClause(SearchColumns);
var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
var where       = ClayDataQuery.CombineWhere(searchWhere, filterWhere);
var orderBy     = query.BuildOrderBy(DefaultOrder);

var rows = await DynamicSql.QueryPagedRowsAsync(...);
```

`query.GroupEnabled` и `query.GroupColumns` не читаются — на выходе всегда плоский список
`ClayDynamicRow`, без `GroupHeaderRow`. В статическом режиме плоский список с заголовками
групп собирает `ClayGridPageBase` (`GroupedPage<T>`, состояние развёрнутости, `TotalEffectiveRows`),
а в динамическом `DataLoader` вообще `null`.

При этом UI группировки включён:

- `InitDynamicMode` ставит всем колонкам вывода `Groupable = true` → в тулбаре есть кнопка
  «Группировать»;
- лоток группировки открывается, колонку в него можно перетащить, `AddGroupColumn` дёргает
  `NotifyQueryChanged` → уходит запрос, данные приходят те же, плоские;
- `RestoreDynamicState` → `ApplySavedGroups` восстанавливает группировку из `grp+gridId`;
- чипы лотка вызывают `DataLoader.IsLevelFullyExpanded(depth)` — в разметке это под
  `@if (DataLoader is not null)`, то есть переключателей уровней нет, но сам чип есть.

Итог: пользователь группирует, ничего не происходит, состояние «сгруппировано» пишется в БД
и молча живёт дальше. Хуже того, `IsGrouped(sqlName)` прячет колонку (`Hidden="@IsGrouped(sqlName)"`
в разметке) — то есть группировка не просто не работает, она УБИРАЕТ колонку из грида.

Полноценная реализация группировки для динамического режима — это отдельная фича уровня G-плана
(сборка `GroupedPage` поверх произвольного `SQL` из `Запросы`, состояние развёрнутости,
`TotalEffectiveRows`, ленивая догрузка потомков). В эту серию багфиксов она не входит: серия
чинит то, что сломано, а группировки в динамическом режиме никогда и не было. Этот шаг убирает
нерабочий UI.

## Изменить/создать

**1.** `ClayGrid.Dynamic.cs`, `InitDynamicMode` — колонки динамического грида не группируемые.
В главном цикле регистрации меты:

```csharp
var meta = new ClayColumnMeta
{
    ColumnId    = col.ColumnId,
    SqlName     = col.Column,
    DisplayName = col.Header ?? col.Column,
    SortName    = col.Column,
    // Группировка в динамическом режиме не реализована: LoadDynamicData не собирает
    // GroupedPage, а DataLoader отсутствует. См. GF14 / GF7_backlog.
    Groupable   = false,
    Filterable  = col.Type != (int)ClayColumnKind.List,
    Type        = desc,
};
```

Блок регистрации фильтр-онли колонок (Тип 6/11) уже ставит `Groupable = false` — не трогай.

Кнопка «Группировать» в тулбаре завязана на `_columnBySqlName.Values.Any(c => c.Groupable)` —
она исчезнет сама, лоток не откроется, drop колонки в лоток станет недостижим. Отдельных
правок в `ClayGrid.razor` не нужно. **Проверь это чтением разметки**, а не на веру.

**2.** `ClayGrid.Dynamic.cs`, `RestoreDynamicState` — не восстанавливать группировку:

```csharp
// Группировка: в динамическом режиме не поддерживается (GF14),
// сохранённое значение игнорируем, но НЕ затираем — оно понадобится,
// когда группировка будет реализована.
```

Вызов `ApplySavedGroups(grpVal)` убрать, сам метод оставить (пригодится). `SaveDynamicState`
не трогать: `grp+gridId` продолжит писаться пустой строкой, так как `_groupColumns` всегда пуст.

## Не делай

Не реализовывай группировку — это отдельный план, не багфикс. Не удаляй `ApplySavedGroups`,
`_groupColumns`, `ClayGrid.Grouping.cs` и ветки `GroupHeaderRow` в разметке — всё это нужно
статическому режиму и понадобится динамическому. Не меняй формат `grp+gridId`. Не трогай
статический режим: там `Groupable` приходит из `RegisterColumn` и остаётся `true`.

## Проверка (ручная)

- `?id=140` → кнопки «Группировать» в тулбаре НЕТ, лотка группировки нет;
- заголовки колонок: меню (⋮) при `ColumnMenuMode="Always"` показывает только «Фильтровать»,
  пункта «Группировать» нет;
- вручную вписать в `vwНастройки` `grp140` = `КодТипа` для `CLID=7`, открыть `?id=140&CLID=7`
  → грид плоский, колонка `Тип исследования` НА МЕСТЕ (а не скрыта `IsGrouped`), ошибок нет;
- после загрузки `grp140` перезаписан пустой строкой, но данные грида не пострадали;
- статический режим (`MedicalTests.razor`): кнопка «Группировать» на месте, группировка
  работает, чипы уровней разворачиваются — регрессии нет;
- в `GF7_backlog.md` пункт про группировку отмечен как «UI отключён в GF14, реализация — новый план».
