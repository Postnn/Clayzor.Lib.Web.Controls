# V2. SQL для `ValueFilter` в `ClayCompositeSqlBuilder`

Научить построитель SQL превращать лист `ValueFilter` (задача V1) во фрагмент
WHERE. Значения — только Dapper-параметрами; имя колонки — только из белого
списка; имена параметров — сквозным счётчиком (как уже сделано для `ColumnFilter`).

## Где
`Filter/ClayCompositeSqlBuilder.cs`. Сейчас `BuildGroup` разбирает узлы через
`switch`: `ClayFilterGroupNode` → `BuildGroup`, `ColumnFilter` → `BuildLeaf`,
`_ => null`. Добавить ветку `ValueFilter vf => BuildValueLeaf(vf, ...)` и метод
`BuildValueLeaf` (сигнатура как у `BuildLeaf`: `parameters`, `knownColumns`,
`columnNameMap`, `ref int counter`).

## Логика `BuildValueLeaf`
1. Белый список: если `!knownColumns.Contains(vf.Column)` → `return null`
   (как в `BuildLeaf`).
2. Если `!vf.HasValue` → `return null`.
3. Резолв имени колонки через `columnNameMap` (как в `BuildLeaf`): `colName`.
4. Набрать параметры для `vf.Values`: на каждый — `p{counter++}`, добавить в
   `parameters` через `parameters.Add(pName, value)`; собрать список `@p..`.
5. Собрать фрагмент по таблице ниже. Скобки обязательны — фрагмент склеивается
   с соседями через AND/OR в `BuildGroup`.

### Семантика (Negate × BlankChecked)
Пусть `IN = colName IN (@p..)`, `NIN = colName NOT IN (@p..)`.

| Negate | BlankChecked | Values пуст? | Фрагмент |
|--------|--------------|--------------|----------|
| false  | false        | нет          | `(IN)` |
| false  | true         | нет          | `(IN OR colName IS NULL)` |
| false  | true         | да           | `(colName IS NULL)` |
| true   | false        | нет          | `(NIN AND colName IS NOT NULL)` |
| true   | true         | нет          | `(NIN OR colName IS NULL)` |
| true   | true         | да           | `(1=1)` → вернуть `null` (условие ничего не отсекает) |

Пояснения:
- В режиме `Negate=false` (IN) невыбранные значения отсекаются; пустые строки
  трактуем как `NULL` — если «(пустые)» не отмечены, они и так не попадут в `IN`.
- В режиме `Negate=true` (NOT IN) по умолчанию SQL `NOT IN` **не** возвращает
  строки с `NULL`, поэтому когда «(пустые)» пользователем **сняты** (не должны
  показываться) — этого достаточно, но чтобы поведение было явным, добавляем
  `AND colName IS NOT NULL`. Когда «(пустые)» отмечены (должны показываться) —
  добавляем `OR colName IS NULL`.
- Пустые строки `''`: считать эквивалентом пустышки. Если тип колонки текстовый,
  «(пустые)» в режиме IN давать `(IN OR colName IS NULL OR colName = '')`, в
  режиме NOT-IN при снятых пустых — `AND colName IS NOT NULL AND colName <> ''`.
  Тип брать не нужно на этом уровне — сделать обработку `''` безопасной для всех
  типов через `colName IS NULL` c дополнительным `OR colName = ''` только когда
  значения в `Values` строковые (проверка `vf.Values` на `string`), иначе не
  добавлять сравнение с `''`. (Достаточно простой эвристики; при сомнении —
  только `IS NULL`.)

Если и `Values` пуст, и `BlankChecked=false` — `HasValue=false`, вернётся `null`
ещё на шаге 2.

## Требование инверсии (треб. 14)
Инверсию (`Negate`) вычисляет **вызывающий код** при применении фильтра (V6/V7),
а не билдер: если из полного набора уникальных значений сняты «условно 3», в
`ValueFilter` кладут `Negate=true` и в `Values` — только снятые значения. Билдер
лишь честно строит `NOT IN`. Здесь ничего вычислять не нужно — только корректно
отработать оба режима.

## Критерии
- [ ] `BuildGroup` распознаёт `ValueFilter` и вызывает `BuildValueLeaf`.
- [ ] Неизвестная колонка → `null`; пустой узел → `null`.
- [ ] Значения только через Dapper-параметры (`@p{n}`), имена уникальны за счёт
      сквозного `counter`.
- [ ] Все шесть строк таблицы Negate×Blank отрабатывают корректно; фрагмент в
      скобках.
- [ ] `ColumnFilter`-ветка и `BuildLeaf` не изменены по поведению.
- [ ] `dotnet build` без ошибок.
