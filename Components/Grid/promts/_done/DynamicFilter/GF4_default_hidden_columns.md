> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF4 — скрытая по умолчанию колонка должна быть зарегистрирована, а не выброшена

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — весь `InitDynamicMode`
(отбор `visibleCols`, блок регистрации фильтр-онли типов 6/11, главный цикл регистрации меты
и cell-шаблонов); `Components/Grid/ClayGrid.razor.cs` — `_columnOrder`, `_hiddenSqlNames`,
`BuildColumnSettingsItems`; `Components/Grid/Dynamic/ClayColumnKind.cs` и `ClayColumnTypeMap.cs`;
`Components/Grid/Dynamic/GridStateSerializer.cs` — формат `cols`; `scripts/dynamic-grid/schema.sql`
— сид грида 140, колонка `Активно` с `Порядок = 0`.

## Дефект

```csharp
var visibleCols = _dynamicCols
    .Where(c => c.Order is > 0)
    .OrderBy(c => c.Order ?? int.MaxValue)
    .ToList();
```

Дальше по файлу `visibleCols` — единственный источник регистрации: только они попадают в
`_columnById`, `_columnBySqlName`, `_columnOrder` и `_cellTemplates`. Колонка с
`Порядок` 0/NULL не скрыта — её в гриде нет вообще:

- её нет в диалоге «Настройка колонок» (`BuildColumnSettingsItems` берёт колонки из
  `_columnBySqlName`) → пользователь не может её включить никогда;
- её нет в `_hiddenSqlNames` → `SerializeColumns` не пишет её в параметр `cols+gridId` →
  состояние «выключена по умолчанию, включена пользователем» негде хранить.

Формат `cols` (`sqlName:0|1`, см. `GridStateSerializer`) построен ровно на том, что скрытая
колонка зарегистрирована и лежит в `_hiddenSqlNames`. Формулировка G4 «Колонки с Order NULL/0 —
скрыты (не в вывод)» означает «не выводятся в гриде», а не «не существуют».

Фильтр-онли типы (`ConditionBool` = 6, `ConditionList` = 11) — отдельная история: они
регистрируются своим блоком выше и в `_columnOrder` не попадают сознательно. Это поведение
сохранить.

## Изменить/создать

`ClayGrid.Dynamic.cs`, `InitDynamicMode`.

**1.** Заменить отбор `visibleCols` на полный список колонок вывода с сохранением дефолтного
порядка (сначала видимые по `Порядок` по возрастанию, затем скрытые):

```csharp
_dynamicCols = await ClayGridDefinitionData.LoadColumnsAsync(Db, gridId, opt.ColumnsTable, opt.Schema);

// Колонки вывода: сначала видимые по Порядок (по возрастанию), затем скрытые (Порядок 0/NULL).
// Фильтр-онли типы (6, 11) в вывод не идут — они регистрируются отдельным блоком ниже.
var gridCols = _dynamicCols
    .Where(c => c.Type != (int)ClayColumnKind.ConditionBool
             && c.Type != (int)ClayColumnKind.ConditionList)
    .OrderBy(c => c.Order is > 0 ? 0 : 1)
    .ThenBy(c => c.Order ?? int.MaxValue)
    .ToList();

var visibleCols = gridCols.Where(c => c.Order is > 0).ToList();

SearchColumns = visibleCols.Select(c => c.Column).ToArray();
DefaultOrder  = string.Join(", ", visibleCols.Select(c => c.Column));
_dynamicKnownColumns = gridCols.Select(c => c.Column).ToHashSet();
```

`SearchColumns` и `DefaultOrder` намеренно остаются на видимых: поиск и дефолтная сортировка
не должны цеплять то, чего пользователь не видит. `_dynamicKnownColumns` — наоборот, полный
список: фильтровать по скрытой колонке можно, она есть в SQL источника (плюс к нему ниже
по коду добавляются фильтр-онли колонки — этот блок не трогай).

**2.** В циклах загрузки справочников (Тип 5 `List` и Тип 9 `Icon`) заменить `visibleCols` на
`gridCols` — справочник нужен и скрытой колонке, её могут включить.

**3.** В главном цикле регистрации заменить `visibleCols` на `gridCols` и добавить пометку
скрытых:

```csharp
foreach (var col in gridCols)
{
    var desc = ClayColumnTypeMap.Resolve(col.Type);
    if (desc is null) continue; // неподдержанный тип — пропускаем с логом

    var meta = new ClayColumnMeta { /* … как было … */ };
    _columnById[col.ColumnId]    = meta;
    _columnBySqlName[col.Column] = meta;
    _columnOrder.Add(col.ColumnId);

    // Порядок 0/NULL — скрыта по умолчанию, но доступна в «Настройке колонок»
    if (col.Order is not > 0)
        _hiddenSqlNames.Add(col.Column);

    /* … кеширование имён и сборка cell-шаблона как было … */
}
```

Порядок внутри `InitDynamicMode` не меняй: регистрация колонок остаётся ДО
`RestoreDynamicState`/`ApplyUrlParams` — те опираются на заполненный `_columnBySqlName`.

## Не делай

Не меняй формат `cols` (`sqlName:0|1`) — он уже лежит в `vwНастройки` у пользователей.
Не добавляй фильтр-онли колонки (Тип 6/11) в `_columnOrder`. Не трогай блок их регистрации.
Не меняй логику мерджа сохранённого состояния — это GF5. Не трогай статический режим.

## Проверка (ручная)

- `?id=140&CLID=9` (чистое состояние) → в гриде колонки `№`, `Название`, `Создано`,
  `Тип исследования` именно в этом порядке; `Активно` (`Порядок = 0`) не выводится;
- открыть «Настройка колонок» → `Активно` ЕСТЬ в списке, чекбокс снят, стоит в конце;
  включить → колонка появилась в гриде последней, значения заполнены;
- F5 → `Активно` осталась включённой (её видимость уехала в `cols140` как `Активно:1`);
- в `ЗапросыКолонки` поменять `Порядок` у двух колонок местами, открыть с новым `CLID` →
  порядок в гриде соответствует новому `Порядок`;
- колонка с `Тип`, для которого `ClayColumnTypeMap.Resolve` возвращает `null`, по-прежнему
  пропускается и не ломает грид;
- колонка Тип 6/11 не появилась ни в гриде, ни в диалоге «Настройка колонок», но доступна
  в диалоге фильтра.
