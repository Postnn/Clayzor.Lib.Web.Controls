> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненных **GF5**, **GF9**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF12 — писать в `vwНастройки` только изменившиеся параметры

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — `SaveDynamicState`,
`RestoreDynamicState`, `_dynamicSavedParams`, `_dynamicForcedParamNames`, `LoadDynamicData`;
`Clayzor.Lib.Entities/DynamicGrid/ClayGridUserParamsData.cs` — `LoadAsync`, `SaveAsync`;
`scripts/dynamic-grid/schema.sql` — триггер `TR_ClayGridUserParams_Upsert`;
`promts/G07_state_persistence.md` — правило «сохранять после каждой загрузки».

## Дефект

`SaveDynamicState()` вызывается в конце `LoadDynamicData`, то есть на КАЖДОЙ загрузке данных:
первое открытие, переход по странице, сортировка, фильтр, обновление. Каждый вызов — пять
безусловных `INSERT` в `vwНастройки`, каждый из которых триггер `INSTEAD OF INSERT`
превращает в `UPDATE`. Пять круговых обращений к БД на любой чих пользователя, включая
листание страниц, где состояние вообще не менялось.

Хуже другое: первая загрузка (GF2) тоже вызывает `SaveDynamicState`. `CLID` при отсутствии
query-параметра равен `0` (`ResolveClientId`), у `Home.razor` его нет. Значит дефолт из
`Порядок` фиксируется в `cols+gridId` при первом же открытии грида, до любого действия
пользователя. После GF5 это уже не ломает функциональность (новые колонки домердживаются),
но означает, что изменения `Порядок` в справочнике не доезжают до пользователей, которые
хоть раз открывали грид.

Правило G7 «сохранять после КАЖДОЙ загрузки» этому не противоречит: сохранять нужно
по-прежнему после каждой загрузки, просто нечего писать, если значение не изменилось.

## Изменить/создать

`ClayGrid.Dynamic.cs`.

**1.** `_dynamicSavedParams` сейчас `IReadOnlyDictionary<string, string>` и после
`RestoreDynamicState` больше не обновляется. Сделать его изменяемым кешем «что сейчас лежит
в БД»:

```csharp
private Dictionary<string, string> _dynamicSavedParams = [];
```

Проверь `ApplyUrlParams` — он читает `_dynamicSavedParams.ContainsKey(...)` для логики
default-параметров с `_`. Смена типа на `Dictionary` это не ломает, но **прочитай и убедись сам**.

**2.** В `RestoreDynamicState` заполнять кеш копией загруженного:

```csharp
_dynamicSavedParams = new Dictionary<string, string>(saved);
```

**3.** `SaveDynamicState` — один helper вместо пяти веток:

```csharp
private async Task SaveDynamicState()
{
    var opt = DynamicOpts.Value;
    var p   = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);

    await SaveParamIfChanged(p(opt.ColumnsParamPrefix),
        GridStateSerializer.SerializeColumns(_columnOrder, _columnById, _hiddenSqlNames), opt);
    await SaveParamIfChanged(p(opt.SortingParamPrefix),
        GridStateSerializer.SerializeSort(_sortState), opt);
    await SaveParamIfChanged(p(opt.GroupingParamPrefix),
        GridStateSerializer.SerializeGroups(_groupColumns), opt);
    await SaveParamIfChanged(p(opt.PageSizeParamPrefix),
        GridStateSerializer.SerializePageSize(_pageSize), opt);
    await SaveParamIfChanged(p(opt.FilterParamPrefix),
        GridStateSerializer.SerializeFilter(_filterRoot) ?? string.Empty, opt);
}

/// <summary>
/// Пишет параметр, только если значение отличается от того, что уже в БД
/// (по кешу <see cref="_dynamicSavedParams"/>). Forced-параметры (из URL) не сохраняются.
/// </summary>
private async Task SaveParamIfChanged(string name, string value, ClayGridDynamicOptions opt)
{
    if (_dynamicForcedParamNames.Contains(name)) return;
    if (_dynamicSavedParams.TryGetValue(name, out var current) && current == value) return;

    await ClayGridUserParamsData.SaveAsync(Db, _dynamicClid, name, value, opt.UserParamsTable, opt.Schema);
    _dynamicSavedParams[name] = value;   // кеш обновляем ТОЛЬКО после успешной записи
}
```

Ключевой момент: `_dynamicSavedParams[name] = value` строго ПОСЛЕ `await SaveAsync`. Если
запись упадёт (`SqlException` перехватит `ISqlErrorHandler`), кеш не должен врать, будто
значение в БД.

Второй момент: `LoadAsync` возвращает только реально существующие в БД строки. Параметра нет
в кеше → `TryGetValue` даёт `false` → первая запись произойдёт. Это правильно для нового
пользователя, но означает, что дефолт всё равно один раз запишется. Убрать это можно только
отказом от правила G7 — не в этом шаге.

## Не делай

Не переставай вызывать `SaveDynamicState` из `LoadDynamicData` — правило G7 остаётся.
Не заменяй пять параметров на один комбинированный. Не убирай `SaveAsync`-по-одному в пользу
батча — триггер `INSTEAD OF INSERT` set-based и это возможно, но это отдельная задача.
Не трогай `_dynamicForcedParamNames`. Не трогай статический режим.

## Проверка (ручная, по SQL-профайлеру)

- `?id=140&CLID=7`, первое открытие для этого CLID → 5 `INSERT` в `vwНастройки` (кеш пуст);
- F5 → **ни одного** `INSERT`: состояние совпало с сохранённым;
- «Вперёд», «Назад», «Обновить» → ни одного `INSERT` (номер страницы не сохраняется, `pgs` —
  это размер, а не номер);
- сменить сортировку → ровно ОДИН `INSERT` (`srt140`), остальные четыре параметра не тронуты;
- выключить колонку → ровно один `INSERT` (`cols140`);
- сменить «Строк на странице» → один `INSERT` (`pgs140`); проверь, что `cols140` не поехал;
- снять фильтр (сценарий GF9) → один `INSERT` (`flt140`) со значением `''`, и он произошёл
  ровно один раз, а не на каждой последующей загрузке;
- уронить БД на середине сессии (или сломать имя таблицы в конфиге) → после восстановления
  изменение состояния всё ещё пишется, то есть кеш не «съел» неудавшуюся запись.
