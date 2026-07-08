# V4. Источник уникальных значений (ленивый, cap 100, контекстный)

Добавить загрузку списка уникальных значений колонки для «Excel-фильтра».
Запрос выполняется **только по требованию** (при раскрытии списка — вызывает V6),
ограничен порогом 100 и учитывает уже применённые фильтры **других** колонок.
Покрывает треб. 2, 3, 10 (порог) и готовит данные для 6 (пустышки).

## Контракт — `IClayGridDataLoader` (`ClayGridPageBase.cs`)
Добавить метод:

```csharp
Task<DistinctValuesResult> LoadDistinctValuesAsync(
    string sqlName, ClayDataQuery query, int limit = 100);
```

Тип результата — новый файл `Components/Grid/DistinctValuesResult.cs`:

```csharp
public sealed class DistinctValuesResult
{
    /// Уникальные значения (без пустышек), не больше limit. Пусто, если Capped.
    public IReadOnlyList<object?> Values { get; init; } = [];
    /// true — уникальных значений больше limit (список клиенту не отдаётся, треб. 3, 10).
    public bool Capped { get; init; }
    /// В колонке есть строки с NULL или пустой строкой → показать пункт «(пустые)».
    public bool HasBlanks { get; init; }
    /// Полное число уникальных не-пустых значений (для инверсии, треб. 14), когда не Capped.
    public int TotalDistinct { get; init; }
}
```

## Область выборки — ВСЯ таблица, НЕ текущая страница
Список уникальных значений формируется по **всему** результату запроса (все
строки грида) с учётом поиска и фильтров **других** колонок. Пагинация
(`query.PageNumber`, `query.PageSize`, любые `OFFSET/FETCH`, `TOP` от размера
страницы) к distinct-запросу **не применяется**. Единственный `TOP` здесь — это
`TOP(@lim)` от порога уникальности (100), а не от размера страницы. Сортировка
страницы (`BuildOrderBy`) тоже не нужна — только `ORDER BY v` для стабильного
списка значений.

## Реализация в `ClayGridPageBase<T>`
Ориентир — `LoadFlatData()` (берём из него построение `where`, но **без** блока
пагинации `GetPagedAsync`):
- `selectSql = Grid?.SelectSql`, `searchColumns = Grid?.SearchColumns`.
- Построить контекстный WHERE **как в LoadFlatData, но без собственного условия
  этой колонки**: взять `query.CompositeFilter`, склонировать дерево и **удалить
  из него все листья (`ColumnFilter` и `ValueFilter`) с `Column == sqlName`**
  (Excel показывает все значения колонки в контексте фильтров прочих колонок).
  Затем `searchWhere = query.BuildWhereClause(searchColumns)` +
  `compositeWhere = BuildCompositeFilterClause(clonedRootWithoutSelf, dp)` →
  `where = ClayDataQuery.CombineWhere(searchWhere, compositeWhere)`.
  `dp.Add("search", $"%{query.SearchText}%")` как в `LoadFlatData`.
- Имя колонки для проекции — сам `sqlName` (выходное имя SELECT), заключать в
  квадратные скобки: `[sqlName]`. Источник — базовый SELECT как подзапрос:
  `FROM ( {selectSql} ) src` + (`WHERE {where}` если не null).
- Порог: сначала посчитать `COUNT(*)` по уникальным не-пустым:
  `SELECT COUNT(*) FROM (SELECT DISTINCT [sqlName] v FROM (…) src WHERE … AND [sqlName] IS NOT NULL AND [sqlName] <> '') t`.
  Если `> limit` → вернуть `Capped=true`, `Values=[]`, `HasBlanks` посчитать
  отдельным дешёвым `EXISTS`.
- Иначе выбрать значения:
  `SELECT DISTINCT TOP (@lim) [sqlName] v FROM (…) src WHERE … AND [sqlName] IS NOT NULL AND [sqlName] <> '' ORDER BY v` (`@lim = limit`).
  Список **всегда сортируется по возрастанию** (`ORDER BY v ASC`): для текстовых
  колонок — по алфавиту, для чисел/дат — по естественному порядку значений.
  Сортировку выполняет SQL (`ORDER BY`) с коллацией БД; в UI (V6) порядок из
  запроса не переставлять. Служебные пункты «(Выделить все)» и «(пустые)» в
  сортировке не участвуют — они всегда первым/последним элементом (см. V6).
- `HasBlanks`: `SELECT CASE WHEN EXISTS(SELECT 1 FROM (…) src WHERE … AND ([sqlName] IS NULL OR [sqlName] = '')) THEN 1 ELSE 0 END`.
- Выполнять через `Db` (`DbManager`, Dapper), тем же способом что и остальные
  запросы страницы (`Db.QueryAsync<...>` / скалярный запрос). Значения читать как
  `object`; сохранить типизацию (bool/date/number/string) — не приводить всё к
  строке (нужно для корректного `IN` в V2).
- Пустую строку `''` объединять с NULL только для текстовых колонок; для
  нетекстовых сравнение с `''` не добавлять (см. V2, та же эвристика). Тип брать
  из `FilterColumnTypes` (см. `ClayGridPageBase.ColumnTypes.cs`,
  `_inferredColumnTypes`).

## ⚠️ Не собирать `WHERE {where} AND …` вручную
`where` (контекстный фильтр) может быть `null`. Нельзя дописывать
`{(where is null ? "" : $"WHERE {where}")} AND [col] IS NOT NULL` — при `where==null`
получится `… src AND [col] IS NOT NULL` → SQL-ошибка «Неправильный синтаксис около
AND». Предикаты «не пусто»/«пусто» складывать в общий WHERE через
`ClayDataQuery.CombineWhere` (он оборачивает операнды в скобки и при `null`
возвращает второй операнд):
```csharp
var notBlank = isText
    ? $"{col} IS NOT NULL AND {col} <> ''"
    : $"{col} IS NOT NULL";
var valueWhere = ClayDataQuery.CombineWhere(where, notBlank); // всегда непусто
// затем: FROM (select) src WHERE {valueWhere}
```
Та же логика для проверки пустышек: `CombineWhere(where, "col IS NULL OR col = ''")`.

## Безопасность
- `sqlName` подставляется в SQL **только** если он есть в реестре колонок
  (`BuildKnownColumns()` / ключи `_inferredColumnTypes`). Иначе — бросить/вернуть
  пустой результат, в SQL произвольное имя не пускать.
- Все пользовательские значения (поиск, значения фильтров прочих колонок) — через
  Dapper-параметры (уже обеспечивается `BuildCompositeFilterClause`).

## Критерии
- [ ] Метод в `IClayGridDataLoader` + реализация в `ClayGridPageBase<T>`.
- [ ] Выборка идёт по всей таблице (весь результат запроса), НЕ по текущей
      странице: пагинация к distinct-запросу не применяется.
- [ ] Собственные условия колонки исключаются из контекстного WHERE.
- [ ] Список значений отсортирован по возрастанию (`ORDER BY v`): текст — по
      алфавиту, числа/даты — по значению; порядок из SQL в UI не переставляется.
- [ ] `Capped=true` при `> limit` без выборки значений; иначе значения ≤ limit.
- [ ] `HasBlanks` определяется отдельно; типизация значений сохранена.
- [ ] `sqlName` только из белого списка; значения — параметрами.
- [ ] `dotnet build` без ошибок.
