# V1. Модель узла фильтра по значению (`ValueFilter`)

Добавить листовой узел дерева фильтра для «Excel-фильтра» по набору выбранных
значений колонки. **Только модель** — без UI и без SQL (SQL — задача V2).
Изменение аддитивное: существующее поведение `ColumnFilter` не трогаем.

## Где
Новый файл `Components/Grid/Filter/ValueFilter.cs`. Узел реализует существующий
интерфейс `Filter.IClayFilterNode` (см. `Filter/IClayFilterNode.cs`), как это
уже делает `ColumnFilter` (см. `ClayDataQuery.cs`).

## Класс `ValueFilter : IClayFilterNode`
Поля:
- `string Column` — SQL-имя колонки (как `ColumnFilter.Column`; проверяется по
  белому списку в V2).
- `List<object?> Values` — **литералы, которые реально уйдут в SQL** (меньшая из
  сторон, см. `Negate`). Не путать с полным списком уникальных значений.
- `bool Negate` — режим построения SQL: `false` → `IN (...)`, `true` →
  `NOT IN (...)`. Заполняется на этапе применения (V6/V7) исходя из треб. 14
  (инверсия, когда не выбрано мало значений). На уровне модели — просто флаг.
- `bool BlankChecked` — отмечен ли пользователем служебный пункт «(пустые)»
  (пустые строки / `NULL`). Семантику NULL/'' в SQL разворачивает V2.
- `string ParamPrefix` — префикс имён Dapper-параметров (напр. `vf_<Column>`);
  фактические имена всё равно назначит сквозной счётчик билдера (V2), поле —
  для отладки/читаемости, в SQL не подставляется.

Свойства/поведение:
- `bool HasValue` → `Values.Count > 0 || BlankChecked` (пустой узел не даёт SQL).
- `IClayFilterNode Clone()` — **глубокая копия**: новый список `Values`
  (скопировать элементы), скопировать `Column`, `Negate`, `BlankChecked`,
  `ParamPrefix`. По образцу `ColumnFilter.Clone()` и
  `ClayFilterGroupNode.Clone()`.
- Транзиентные UI-флаги (если понадобятся) помечать
  `[System.Text.Json.Serialization.JsonIgnore]` и не копировать в `Clone()` —
  как `ColumnFilter.IsNew`.

## Важно
- Ничего не менять в `ClayCompositeSqlBuilder` — узел там пока просто попадёт в
  ветку `_ => null` (это ожидаемо до V2, дерево остаётся валидным).
- XML-док на русском на класс и все публичные члены (стиль как в
  `ClayFilterGroupNode.cs` / `ColumnFilter`).

## Критерии
- [ ] `ValueFilter` реализует `IClayFilterNode`, лежит в неймспейсе
      `Clayzor.Lib.Web.Controls.Components.Grid.Filter`.
- [ ] `Clone()` — независимая глубокая копия (правка копии не трогает оригинал,
      в т.ч. список `Values`).
- [ ] `HasValue` учитывает и значения, и `BlankChecked`.
- [ ] `ClayCompositeSqlBuilder` и `ColumnFilter` не изменены.
- [ ] `dotnet build` без ошибок.
