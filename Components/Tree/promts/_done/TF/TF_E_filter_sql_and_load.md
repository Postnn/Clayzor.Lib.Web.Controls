> Часть серии **TF**. Прочитать `TF0_README_tree_filter.md`, отчёты **TF_A**, результаты
> **TF_C**, **TF_D**. Делать ТОЛЬКО этот шаг. **Ядро серии, высокий риск** — перед началом
> git-коммит как точка отката.

# TF_E — SQL режима фильтра: совпадения + предки + флаги, загрузка набора

Строим третий режим загрузки дерева — **фильтр**. SQL: наложить условия → взять совпадения
`TOP(@max+1)` → добрать всех предков → пометить каждую ноду флагами. Дерево строится из набора
целиком. Пометки и подсветку **не рисуем** (это TF_F) — здесь только данные и флаги в модели.

## Модель (из оркестратора, не пересматривать)

- Nested — предки диапазоном `[L..R]`; ParentKey — рекурсивным CTE вверх.
- `MaxFilterRecords` ограничивает **совпадения** (`TOP(@max+1)`), предки не в счёт.
- Флаги в наборе: `_isMatch` (нода сама совпала), `_hasMatchChildren` (есть совпавший потомок).
- Верхний уровень (корни) выводится всегда (правило 1); предки совпавших — всегда (правило 2).
- Дефолтный фильтр сюда **не** попадает (он WHERE в ленивом режиме — TF_G).

## Прочитать

- отчёт **TF_A** пункты 1,3,4,6 — форма запроса, CTE вверх, диапазон предков, признак совпадения;
- `Clayzor.Lib.Entities/Tree/ClayTreeSqlBuilder.cs`, `ClayTreeData.cs` — куда добавлять;
- `Components/Filter/ClayCompositeSqlBuilder.cs` — `Build(root, parameters, knownColumns,
  columnNameMap?)`: он строит фрагмент WHERE из дерева фильтра. **Переиспользуем его** для
  условия совпадения — не пишем свой разбор дерева фильтра;
- `Components/Tree/Models/ClayTreeNode.cs` — добавить флаги;
- `Components/Tree/ClayTreeView.Loading.cs`, `DataSources/ClaySqlTreeDataSource.cs`.

## 1. Модель узла

В `ClayTreeNode` добавить:

```csharp
/// <summary>Узел сам удовлетворяет условиям фильтра (для пометки «(!)»).</summary>
public bool IsMatch { get; set; }

/// <summary>Среди потомков узла есть удовлетворяющие фильтру (для пометки «(отфильтровано)»).</summary>
public bool HasMatchChildren { get; set; }
```

В `ClayTreeRow` (Entities) — соответствующие `bool` поля и псевдонимы `[_ismatch]`,
`[_hasmatchchildren]` (const, рядом с прочими алиасами).

## 2. SQL — билдер режима фильтра

Новый метод в `ClayTreeSqlBuilder` (чистая функция, тестируется без БД):

```csharp
/// <summary>
/// SQL режима фильтра: совпадения (TOP @max+1) + все их предки, с флагами _ismatch/_hasmatchchildren.
/// whereClause — фрагмент условия из ClayCompositeSqlBuilder (без слова WHERE), параметры — общие.
/// </summary>
public static string BuildFilterSql(ClayTreeSource src, string whereClause, int max);
```

**ParentKey — рекурсивный CTE вверх** (2008 R2, `WITH ... UNION ALL`). Опираемся на пример
заказчика, но идём ВВЕРХ (якорь — совпадения, рекурсия по `parent.[Id] = child.[Parent]`):

```sql
WITH Src AS ( SELECT * FROM (<SelectSql>) x ),
Matches AS (
    SELECT TOP (@max + 1) s.[Id] AS Id, s.[Parent] AS Parent
    FROM Src s
    WHERE <whereClause>              -- из ClayCompositeSqlBuilder
),
Chain AS (
    -- якорь: сами совпадения
    SELECT m.Id, m.Parent, CAST(1 AS bit) AS IsMatchSeed
    FROM Matches m
    UNION ALL
    -- вверх: родитель текущего узла цепочки
    SELECT p.[Id], p.[Parent], CAST(0 AS bit)
    FROM Src p
    INNER JOIN Chain c ON p.[Id] = c.Parent      -- p — родитель c
),
Agg AS (
    -- узел мог прийти и как совпадение, и как предок → сворачиваем
    SELECT Id, MAX(CAST(IsMatchSeed AS int)) AS IsMatch
    FROM Chain GROUP BY Id
)
SELECT s.[Id] AS [_id], s.[Text] AS [_text], s.[Parent] AS [_parent],
       a.IsMatch AS [_ismatch],
       CASE WHEN EXISTS (SELECT 1 FROM Agg ch
                          JOIN Src cs ON cs.[Id] = ch.Id
                          WHERE cs.[Parent] = s.[Id] AND ch.IsMatch = 1)
            THEN 1 ELSE 0 END AS [_hasmatchchildren]
FROM Src s
JOIN Agg a ON a.Id = s.[Id]
ORDER BY <OrderBy>
```

(Имена колонок — из `src.Schema`, всё в квадратных скобках; `Id`/`Text`/`Parent` в примере —
плейсхолдеры. `_hasmatchchildren` считается как «есть прямой ребёнок, который в наборе и сам
совпал» — этого достаточно для пометки «(отфильтровано)» у прямого родителя; для более глубокой
семантики см. ловушку 3.)

**Nested — без рекурсии, диапазоном:**

```sql
WITH Src AS ( SELECT * FROM (<SelectSql>) x ),
Matches AS (
    SELECT TOP (@max + 1) s.[L] AS L, s.[R] AS R, s.[Id] AS Id
    FROM Src s
    WHERE <whereClause>
)
SELECT s.[Id] AS [_id], s.[Text] AS [_text], s.[Parent] AS [_parent],
       CASE WHEN EXISTS (SELECT 1 FROM Matches m WHERE m.Id = s.[Id]) THEN 1 ELSE 0 END AS [_ismatch],
       CASE WHEN EXISTS (SELECT 1 FROM Matches m WHERE m.L > s.[L] AND m.R < s.[R]) THEN 1 ELSE 0 END AS [_hasmatchchildren]
FROM Src s
WHERE EXISTS (SELECT 1 FROM Matches m WHERE m.Id = s.[Id])              -- сам совпал
   OR EXISTS (SELECT 1 FROM Matches m WHERE s.[L] < m.L AND s.[R] > m.R) -- предок совпавшего
ORDER BY s.[L]
```

В Nested `_hasmatchchildren` через диапазон честно означает «есть совпавший потомок на любой
глубине» — это точнее, чем в ParentKey. Разница задокументирована в ловушке 3.

## 3. Исполнение и построение набора

- `ClayTreeData` — метод `LoadFilteredAsync(db, src, whereClause, dp, max, ct)`:
  строит `BuildFilterSql`, выполняет через `DynamicSql.QueryRowsAsync`, маппит в `ClayTreeRow`
  с новыми флагами. `whereClause` и `dp` приходят готовыми (собраны из `_filterRoot` через
  `ClayCompositeSqlBuilder.Build` — вызвать его в Controls, передать результат в Entities);
- `ClaySqlTreeDataSource` — метод, отдающий **плоский** список `ClayTreeNode` с флагами;
- в компоненте (`ClayTreeView.Loading` или новый `ClayTreeView.Filtering.cs`) — собрать из
  плоского списка **дерево**: сгруппировать по `ParentId`, разложить `Children`, проставить
  `Parent`, `Level` (по позиции в собранном дереве, как в обычном маппинге — от родителя).
  Все ноды набора — `IsLoaded = true` (дети уже в наборе), `IsExpanded = true` (показываем
  раскрытым, чтобы совпадения были видны);
- определить, **сработал ли лимит**: пришло ли `> max` совпадений (считать `_isMatch == true`
  до сворачивания — либо отдельным `COUNT`, либо по факту `TOP(@max+1)` вернул max+1 совпадений).
  Сохранить в поле `_filterMatchCount` и `_filterCapped` для панели (TF_D заготовил контейнер).

## 4. Подключение к панели и переключение режимов

- `ApplyFilterAsync` (заглушка из TF_D) заменить на настоящую: если `_filterRoot` непустой и
  **не** только-дефолтный (проверка «только дефолтный» — TF_G; в TF_E считать любой непустой
  фильтр пользовательским) → режим фильтра (собрать набор); иначе → обычная ленивая загрузка;
- вторая строка панели: показать «Найдено записей: N» из `_filterMatchCount`; при `_filterCapped`
  — текст «Найдено более N записей…». Наполнить заготовку TF_D;
- кнопка удаления фильтра → сбросить `_filterRoot`, `_filterMatchCount`, вернуть обычный режим.

## Ловушки

1. **`whereClause` для WHERE и для набора — один и тот же текст и одни параметры.** Собрать
   `ClayCompositeSqlBuilder.Build` **один раз**, использовать во всех вхождениях
   (`Matches.WHERE`). Белый список колонок (`knownColumns`) — SqlName фильтруемых колонок дерева
   (из TF_C), иначе билдер отвергнет колонку.
2. **`TOP` без `ORDER BY` в `Matches`** даёт недетерминированный набор совпадений при
   превышении лимита. Для счётчика «более N» это неважно (нам нужен только факт > max), но если
   важно, какие именно 100 показать — добавить `ORDER BY` в `Matches` (по TextColumn/L).
   Решить и отметить.
3. **`_hasmatchchildren`: прямой ребёнок vs любой потомок.** В Nested — любой потомок
   (диапазон). В ParentKey пример считает прямого ребёнка. Для пометки «(отфильтровано)»
   (правило 4: «содержит отфильтрованные ноды») достаточно «есть совпавший потомок на любой
   глубине». Привести ParentKey к той же семантике: в `Agg`/финальном CASE учитывать не только
   прямых детей, а весь подсписок цепочки под узлом (узел является предком совпавшего = узел
   есть в `Chain` с путём к какому-то `IsMatch=1`). Реализовать через тот же `Chain`:
   узел с `IsMatch=0`, попавший в цепочку, **по определению** предок какого-то совпадения →
   `_hasmatchchildren = (IsMatch=0)`. Это проще и вернее: в наборе ParentKey не-совпавший узел
   присутствует **только** как предок, значит у него всегда есть совпавший потомок. Проверить
   на данных и фиксировать в тесте.
4. **MARS/реентерабельность.** Весь набор — один запрос (CTE), не цикл по нодам. Не запускать
   параллельно с ленивыми запросами.
5. **Пустой результат фильтра.** Совпадений нет → набор = только корни (правило 1: верхний
   уровень всегда). Проверить, что SQL возвращает корни даже при нуле совпадений, либо
   догрузить корни отдельно. Отметить принятое решение.

## Не делай

- Не рисуй пометки/подсветку (TF_F) и не трогай выделение/состояние (TF_I).
- Не реализуй дефолтный фильтр как отдельный режим (TF_G) — здесь любой непустой фильтр
  считается пользовательским.
- Не пиши свой разбор дерева фильтра — только `ClayCompositeSqlBuilder`.
- Не выполняй SQL в Controls — `BuildFilterSql` и исполнение в Entities; условие собирается в
  Controls и передаётся строкой+параметрами.
- Не грузи набор построчно/циклом — один CTE-запрос.

## Проверка

**Тесты `ClayTreeSqlBuilderTests`** (без БД):
- `BuildFilterSql` ParentKey: содержит `WITH`, `UNION ALL`, `TOP (@max + 1)`, соединение вверх
  `p.[Parent-col] = c` по нужным колонкам; `_ismatch`, `_hasmatchchildren` в SELECT;
- `BuildFilterSql` Nested: **нет** `UNION ALL` (без рекурсии); предки через `[L] <`/`[R] >`;
  `TOP (@max + 1)`; оба флага через `EXISTS`/диапазон;
- все идентификаторы в скобках; значения — только параметры (плюс `@max`), литералов условий нет;
- `whereClause` подставлен и в `Matches`, дублирования разных текстов условия нет.

**Ручной прогон** (`/tree-test`, оба режима, `MaxFilterRecords` временно = 3 для проверки лимита):
- фильтр «Название содержит Ноутбук» → в дереве видны оба ноутбука, их родители (Ноутбуки →
  Компьютерная техника → Оборудование), корни; посторонние ветки (Мебель, ПО) не показаны или
  показаны только как корни (по правилу 1) — зафиксировать ожидание;
- в модели: у ноутбуков `IsMatch=true`; у «Ноутбуки»/«Компьютерная техника»/«Оборудование»
  `IsMatch=false`, `HasMatchChildren=true` (пометки ещё не рисуются — проверить отладкой/логом);
- один SQL-запрос на установку фильтра (профайлер), не цикл;
- `MaxFilterRecords=3`, фильтр с >3 совпадениями → `_filterCapped=true`, панель: «Найдено более
  3 записей…»; ≤3 → «Найдено записей: N» (N = число совпадений, предки не в счёт);
- Nested и ParentKey на одном фильтре → одинаковый видимый набор;
- снять фильтр → дерево вернулось в обычный ленивый режим;
- грид не затронут.
