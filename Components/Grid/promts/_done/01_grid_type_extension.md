# 01. Расширение типов грида: Date / Decimal / value-picker

## Контекст (как тип резолвится сейчас)
- Колонки на странице объявляются `ClayColumnDef` **без типа данных**
  (`ColumnId`, `SqlName`, `DisplayName`, `Groupable`, `Filterable`).
- Тип фильтра берётся из `FilterColumnTypes: IReadOnlyDictionary<string, ColumnType>`,
  ключ — **SqlName** (из `[Column("...")]` на свойстве сущности, иначе имя свойства).
  Вычисляется рефлексией в `ClayGridPageBase.InferFilterColumnTypes()` / `MapClrTypeToColumnType`.
- Текущий `MapClrTypeToColumnType`:
  - `bool → Boolean`;
  - `int/long/short/byte/decimal/float/double/uint/ulong/ushort → Number`;
  - всё остальное (включая `DateTime`, C#-`enum`) → `Text`.
- Редактор `Number` в `ClayColumnFilterDialog` — `MudNumericField<int?>`.

Отсюда два реальных дефекта/пробела (а не «фичи на пустом месте»):
1. **Decimal сейчас фильтруется целочисленным редактором** (`decimal`→`Number`→`MudNumericField<int?>`) — усечение дробной части.
2. **Дата не поддержана** — `DateTime` уходит в `Text`, фильтруется как строка.

> В текущем гриде «Медицинские исследования» все колонки — Text/Number/Boolean
> (Тип, Код, Название, Порядок — текст/число; Группа, Заключение — bool).
> Расширения ниже — **на вырост**; этим гридом они пока не задействуются.

## Что делаем

### A. Дата (новый тип) — `ColumnType.Date`
- `enum ColumnType` += `Date`.
- `MapClrTypeToColumnType`: `DateTime`, `DateTimeOffset`, `DateOnly` → `Date`.
- `ColumnFilterOperatorList.DateOperators` = `Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual` (+ опц. `IsNull, IsNotNull`).
- `ClayColumnFilterDialog`: редактор `MudDatePicker` (хранить `DateTime? _date1/_date2`), ветки в `RestoreValue/GetValue/ClauseHasValue`; формат отображения `dd.MM.yyyy`.
- SQL (`BuildSingleClause`): значение даты — Dapper-параметром (как и сейчас для Equals/сравнений); никакой конкатенации. Нормализация — инвариантная культура.

### B. Дробное число — `ColumnType.Decimal`
- `enum ColumnType` += `Decimal`.
- `MapClrTypeToColumnType`: `decimal`, `double`, `float` → `Decimal`
  (вынести из ветки `Number`; целочисленные остаются `Number`).
- `ColumnFilterOperatorList.DecimalOperators` = как `NumberOperators`.
- `ClayColumnFilterDialog`: редактор `MudNumericField<decimal?>` (`_dec1/_dec2`); ветки в `RestoreValue/GetValue/ClauseHasValue`.
- SQL: значение — Dapper-параметром; парсинг/формат — инвариантная культура.
- (Альтернатива, если не хочется новый `ColumnType`: сделать редактор `Number` типобезопасным по фактическому CLR-типу. Но отдельный `Decimal` проще и явнее.)

### C. Операторы NULL — `IsNull` / `IsNotNull`
- `enum ColumnFilterOperator` += `IsNull`, `IsNotNull` (для не-строковых типов, где строковые `IsEmpty`/`IsNotEmpty` = `IS NULL OR = ''` не подходят).
- `BuildSingleClause`: `IsNull → "{colName} IS NULL"`, `IsNotNull → "{colName} IS NOT NULL"` (без параметра).
- `IsEmptyOrNotEmpty` в диалоге расширить до «оператор без значения»: `IsEmpty, IsNotEmpty, IsNull, IsNotNull` (для всех скрывать редактор значения).
- Подписи в `GetOperatorLabel`/`DescribeClause`: «пусто (NULL)», «не пусто (NOT NULL)».

### D. Value-picker вместо «enum» (необязательно)
Раз enum в БД не хранится, отдельный «enum как целочисленный код» **не вводим**.
Вместо этого — необязательный выпадающий выбор значения для **Text/Number**-колонки,
когда у колонки есть заранее известный список вариантов (тип, группа, статус и т.п.):

- Источник вариантов — необязательный маппинг на странице/в базовом классе:
  `protected virtual IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>> FilterLookupOptions`
  (ключ — SqlName; `ClayFilterOption { object? Value; string Label; }`).
  Грид прокидывает его в диалоги (рядом с `FilterColumnTypes`).
- Если для колонки задан список вариантов — редактор значения рендерит выпадающий
  список (по образцу существующего `ClayComboBox`/`ILookupEntity`) вместо `MudTextField`/`MudNumericField`.
  **`ColumnType` и SQL не меняются** — в параметр уходит выбранное `Value` (строка или число),
  сравнение `= @p` / `<> @p` как обычно.
- Это не C#-enum и не `Enum.GetValues` — список приходит из данных/справочника.

## Критерии
- [ ] Существующие Text/Number(int)/Boolean работают без изменений.
- [ ] Decimal-колонка редактируется `MudNumericField<decimal?>` и не усекается; в SQL уходит параметром.
- [ ] Date-колонка фильтруется `MudDatePicker`; парсинг инвариантный; в SQL — параметром.
- [ ] `IsNull`/`IsNotNull` → `IS NULL`/`IS NOT NULL` без параметра; редактор скрыт.
- [ ] (Если делается D) колонка со списком вариантов показывает выпадающий выбор; SQL не меняется.
- [ ] `dotnet build` без ошибок.

## Замечание (улучшение, не обязательно)
В `BuildSingleClause` для `Contains/StartsWith/EndsWith` значение подставляется в LIKE
без экранирования `%`/`_`. Не SQL-инъекция (значения параметризованы), но «протекание»
шаблона LIKE — можно экранировать. Отдельная мелкая задача.
