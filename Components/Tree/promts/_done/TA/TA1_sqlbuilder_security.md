# TA1 — ClayTreeSqlBuilder: инъекция в ORDER BY, циклы CTE, флаг `_hasmatchchildren`

## Контекст

Файл `Clayzor.Lib.Entities/Tree/ClayTreeSqlBuilder.cs`. Три дефекта:

1. **Инъекция через `OrderBy`.** `BuildOrderBy` извлекает имя колонки (текст до первого пробела) и
   проверяет его по белому списку, но **хвост после имени не проверяется** и попадает в SQL как есть.
   Примеры пробоя:
   - `OrderBy = "Name DESCX; DROP TABLE Users"` → имя `Name` проходит проверку, в SQL уходит
     `[Name] DESCX; DROP TABLE Users`.
   - `OrderBy = "[Name] DESC; DROP TABLE Users"` → ветка `trimmed.StartsWith("[")` возвращает
     `bracketed = trimmed` целиком, без какой-либо проверки хвоста.
2. **ParentKey: рекурсивный CTE без защиты от циклов.** Если в данных есть цикл
   (A.Parent = B, B.Parent = A), `Chain` в `BuildParentKeyFilterSql` рекурсирует до лимита
   SQL Server (100) и падает с ошибкой 530.
3. **ParentKey: `[_hasmatchchildren]` вычислен неверно.** Сейчас
   `CASE WHEN a.IsMatch = 0 THEN 1 ELSE 0 END`: узел, который сам совпал с фильтром И имеет
   совпавших потомков, получает `_hasmatchchildren = 0`. В NestedSet-версии оба флага
   независимы — поведение должно совпадать.

Дополнительно: в `BuildNestedSetFilterSql` объявлена неиспользуемая переменная `orderBy` — удалить.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Entities/Tree/ClayTreeSqlBuilder.cs`

**1.1. Переписать разбор частей в `BuildOrderBy`.** Заменить тело цикла `foreach (var part in parts)`
на строгий разбор «имя + необязательное направление», где направление проверяется по белому списку:

```csharp
foreach (var part in parts)
{
    var trimmed = part.Trim();

    // Разбираем строго: [Имя] или Имя, затем опционально ASC/DESC.
    string name;
    string direction = "";

    if (trimmed.StartsWith('['))
    {
        var close = trimmed.IndexOf(']');
        if (close < 0)
            throw new InvalidOperationException($"Незакрытая скобка в ORDER BY: '{trimmed}'.");
        name = trimmed[1..close];
        direction = trimmed[(close + 1)..].Trim();
    }
    else
    {
        var spaceIdx = trimmed.IndexOf(' ');
        name = spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
        direction = spaceIdx > 0 ? trimmed[(spaceIdx + 1)..].Trim() : "";
    }

    if (!knownColumns.Contains(name))
        throw new InvalidOperationException($"Колонка '{name}' из ORDER BY не найдена в схеме источника дерева. Допустимые колонки: {string.Join(", ", knownColumns)}.");

    // Направление — только пусто, ASC или DESC. Всё остальное — отказ.
    if (direction.Length > 0
        && !direction.Equals("ASC", StringComparison.OrdinalIgnoreCase)
        && !direction.Equals("DESC", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Недопустимое направление сортировки '{direction}' в ORDER BY (ожидается ASC или DESC).");

    checkedParts.Add(direction.Length > 0 ? $"[{name}] {direction.ToUpperInvariant()}" : $"[{name}]");
}
```

Старый код с `Replace("[", "")`, переменными `bracketed`/`suffix` — удалить полностью.
Результат всегда собирается заново из проверенных `name` и `direction`, исходная строка
пользователя в SQL не попадает ни в каком виде.

**1.2. Защита от циклов в `BuildParentKeyFilterSql`.** В CTE `Chain` добавить счётчик глубины
и ограничение. Заменить сборку `Chain` на:

```csharp
sb.Append("Chain AS (");
sb.Append("SELECT m.Id, m.Parent, CAST(1 AS bit) AS IsMatchSeed, 0 AS Lvl FROM Matches m");
sb.Append(" UNION ALL");
sb.Append(" SELECT p.").Append(id).Append(", p.").Append(parent).Append(", CAST(0 AS bit), c.Lvl + 1");
sb.Append(" FROM Src p INNER JOIN Chain c ON p.").Append(id).Append(" = c.Parent");
sb.Append(" WHERE c.Lvl < 99");
sb.Append("),");
```

**1.3. Исправить флаги в `Agg`.** Заменить:

```csharp
sb.Append("Agg AS (");
sb.Append("SELECT Id, MAX(CAST(IsMatchSeed AS int)) AS IsMatch FROM Chain GROUP BY Id");
sb.Append(")");
```

на:

```csharp
sb.Append("Agg AS (");
sb.Append("SELECT Id, MAX(CAST(IsMatchSeed AS int)) AS IsMatch, MAX(CASE WHEN IsMatchSeed = 0 THEN 1 ELSE 0 END) AS HasMatchChildren FROM Chain GROUP BY Id");
sb.Append(")");
```

и в финальном SELECT заменить
`CASE WHEN a.IsMatch = 0 THEN 1 ELSE 0 END AS [_hasmatchchildren]` на
`a.HasMatchChildren AS [` + `AliasHasMatchChildren` + `]` (собрать через `sb.Append`, как остальные поля).

Пояснение: строка с `IsMatchSeed = 0` появляется в `Chain` ровно тогда, когда узел — предок
какого-то совпадения, т.е. у него есть совпавшие потомки. Это и есть корректное значение флага,
независимое от `IsMatch`.

**1.4. Удалить неиспользуемую переменную** `var orderBy = BuildOrderBy(src);` из
`BuildNestedSetFilterSql` (ORDER BY там жёстко по левому ключу — это правильно, переменная не нужна).

## Критерии приёмки

- Тесты: `BuildOrderBy("Name DESC")` → `[Name] DESC`; `BuildOrderBy("[Name] desc")` → `[Name] DESC`;
  `BuildOrderBy("Name; DROP TABLE x")` → `InvalidOperationException`;
  `BuildOrderBy("[Name] DESC; DROP TABLE x")` → `InvalidOperationException`;
  `BuildOrderBy("Name DESCX")` → `InvalidOperationException`.
- SQL из `BuildParentKeyFilterSql` содержит `c.Lvl < 99` и `a.HasMatchChildren`.
- Существующие 4 теста `BuildFilterSql` обновлены под новый текст SQL и проходят.
