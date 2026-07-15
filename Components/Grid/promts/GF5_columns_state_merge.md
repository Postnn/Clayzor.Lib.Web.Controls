> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненного **GF4**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF5 — сохранённое состояние колонок мерджится с дефолтом, а не затирает его

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — `RestoreDynamicState`,
`ApplySavedColumns`, `ApplyUrlParams`, `ApplyUrlColumnsValue`, `SaveDynamicState`;
`Components/Grid/Dynamic/GridStateSerializer.cs` — `SerializeColumns`/`DeserializeColumns`;
`Clayzor.Lib.Entities/DynamicGrid/ClayGridUserParamsData.cs` — `LoadAsync` (что возвращается
для отсутствующего параметра), `BuildParamName`; `Kesco.App.Web.Inventory/appsettings.json` —
`UserParamsTable`, `ClientIdQueryParam`.

## Дефект

`ApplySavedColumns` и `ApplyUrlColumnsValue` — два почти одинаковых метода, оба делают:

```csharp
_hiddenSqlNames.Clear();
_columnOrder.Clear();

foreach (var (sqlName, visible) in cols)
{
    if (_columnBySqlName.TryGetValue(sqlName, out var meta))
    {
        _columnOrder.Add(meta.ColumnId);
        if (visible == 0)
            _hiddenSqlNames.Add(sqlName);
    }
}
```

То есть дефолт из `Порядок` сносится целиком, а восстанавливается только то, что перечислено
в сохранённой строке. Последствия:

- колонка, добавленная в `ЗапросыКолонки` после того, как пользователь один раз открыл грид,
  в `cols+gridId` отсутствует → в `_columnOrder` не попадёт → её не будет ни в гриде, ни в
  диалоге настройки. Навсегда;
- фильтр-онли колонка (Тип 6/11) есть в `_columnBySqlName`, но в `_columnOrder` быть не должна;
  если её имя попадёт в строку состояния, она уедет в вывод;
- дубли в строке состояния добавляются в `_columnOrder` дважды.

Плюс общий контекст, который делает это критичным: `SaveDynamicState()` вызывается в конце
`LoadDynamicData`, то есть после КАЖДОЙ загрузки данных, а `CLID` при отсутствии query-параметра
равен `0` (`ResolveClientId`). У `Home.razor` `CLID` нет. Значит `cols+gridId` фиксируется в
`vwНастройки` при первом же открытии грида, до любых действий пользователя — и дальше дефолт
из `Порядок` не применяется в принципе. Это и есть баг «не используется настройка колонок
по умолчанию».

## Изменить/создать

`ClayGrid.Dynamic.cs`.

**1.** Удалить `ApplySavedColumns` и `ApplyUrlColumnsValue`, вместо них — один метод:

```csharp
/// <summary>
/// Применяет строку состояния колонок (из ClayGridUserParams или URL) ПОВЕРХ дефолта
/// из определения. Колонки, которых нет в строке состояния, сохраняют дефолтную
/// видимость и добавляются в конец — иначе новая колонка в ЗапросыКолонки никогда
/// не появится у пользователя с сохранённым состоянием.
/// </summary>
private void ApplyColumnsState(string value)
{
    var cols = GridStateSerializer.DeserializeColumns(value);
    if (cols.Count == 0) return;

    var defOrder  = _columnOrder.ToList();
    var defHidden = _hiddenSqlNames.ToHashSet();

    _columnOrder.Clear();
    _hiddenSqlNames.Clear();

    foreach (var (sqlName, visible) in cols)
    {
        if (!_columnBySqlName.TryGetValue(sqlName, out var meta)) continue;
        if (!defOrder.Contains(meta.ColumnId)) continue;      // фильтр-онли в вывод не пускаем
        if (_columnOrder.Contains(meta.ColumnId)) continue;   // дубли в строке состояния
        _columnOrder.Add(meta.ColumnId);
        if (visible == 0)
            _hiddenSqlNames.Add(sqlName);
    }

    // Колонки определения, которых нет в состоянии, — в конец с дефолтной видимостью
    foreach (var id in defOrder)
    {
        if (_columnOrder.Contains(id)) continue;
        _columnOrder.Add(id);
        if (_columnById.TryGetValue(id, out var meta) && defHidden.Contains(meta.SqlName))
            _hiddenSqlNames.Add(meta.SqlName);
    }

    _dataKey++;
}
```

`defOrder`/`defHidden` — снимок дефолта, собранного в `InitDynamicMode` (GF4). Метод вызывается
только оттуда, после регистрации колонок, поэтому снимок всегда актуален.

**2.** Заменить вызовы:

- `RestoreDynamicState`: `ApplySavedColumns(colsVal)` → `ApplyColumnsState(colsVal)`;
- `ApplyUrlParams`: `ApplyUrlColumnsValue(forcedCols)` → `ApplyColumnsState(forcedCols)`,
  `ApplyUrlColumnsValue(qs[defColsParamName]!)` → `ApplyColumnsState(qs[defColsParamName]!)`.

Логика forced/default (`cols140` без `_` применяется всегда, `_cols140` — только при отсутствии
сохранённого) остаётся без изменений.

## Не делай

Не меняй формат `cols` (`sqlName:0|1`) — он уже в БД. Не меняй правило G7 «сохранять после
каждой загрузки» и не трогай `SaveDynamicState` — оптимизация записи вынесена в `GF7_backlog.md`.
Не трогай восстановление сортировки/группировки/фильтра/размера страницы. Не трогай статический
режим.

## Проверка (ручная + юнит)

Юнит-тест на `ApplyColumnsState` не пишется (метод приватный и завязан на состояние грида) —
проверяй через `GridStateSerializer` round-trip (TG6) и ручные сценарии:

- `?id=140&CLID=7` → выключить `Название`, перетащить `Создано` в начало, F5 → порядок и
  видимость восстановились; `?id=140&CLID=9` → дефолт из `Порядок`, состояние CLID=7 не влияет;
- **ключевой сценарий**: при живом `cols140` для `CLID=7` добавить в `ЗапросыКолонки` новую
  колонку с `Порядок = 2`, открыть `?id=140&CLID=7` → новая колонка появилась В КОНЦЕ, видима,
  остальной пользовательский порядок и видимость не сбросились;
- то же с новой колонкой `Порядок = 0` → появилась в конце и скрыта;
- вручную вписать в `vwНастройки` значение `cols140` = `Название:1,Название:1,НетТакой:1` →
  грид не падает, `Название` одна, остальные колонки определения добавлены в конец;
- вписать в `cols140` имя фильтр-онли колонки (Тип 6/11) → она в гриде не появилась;
- `?id=140&CLID=7&cols140=Название:1,КодИсследования:1` (forced) → применён URL, и после
  перезагрузки БЕЗ этого параметра вернулось сохранённое состояние (forced не сохраняется).
