# CTF1 — NestedSet без `LevelColumn`: грузить прямых детей, а не всё поддерево

> Заплатка к CT1. Читать `CTF0_README_fixes.md`. **Блокер** — из-за него дерево в режиме
> `NestedSet` не работает глубже первого уровня. Делать первым. Правила — `AGENTS.md`.

## Симптом

В режиме `NestedSet` при раскрытии узла второго и глубже уровней в дерево попадают **все
потомки поддерева на всех уровнях сразу**, вперемешку. Пользователь видит это как «ниже
первого уровня дерево ломается».

## Корневая причина (проверено по коду)

`LevelColumn` в структуре таблиц заказчика **отсутствует и не используется**. В
`ClayTreeSqlBuilder.BuildNestedSetSql`, не-корневая ветка, предикат уровня добавляется только
`if (src.Schema.LevelColumn is not null)`:

```csharp
sb.Append(" WHERE s.[").Append(src.Schema.LeftColumn).Append("] > @").Append(LeftParam);
sb.Append(" AND s.[").Append(src.Schema.RightColumn).Append("] < @").Append(RightParam);
if (src.Schema.LevelColumn is not null)
    sb.Append(" AND s.[").Append(src.Schema.LevelColumn).Append("] = @").Append(LevelParam).Append(" + 1");
```

Без `LevelColumn` остаётся `WHERE [L] > @left AND [R] < @right` — это **весь диапазон
поддерева**, а не прямые дети. Во вложенных множествах «прямые дети» без колонки уровня одним
диапазонным условием невыразимы — ровно поэтому CT1 (решение 9) делал Level обязательным. Раз
Level не будет никогда, предикат прямых детей нужно строить иначе.

## Решение

Прямой ребёнок узла-диапазона `(@left, @right)` — это узел внутри диапазона, для которого **нет
промежуточного предка** внутри того же диапазона:

```sql
WHERE s.[L] > @left AND s.[R] < @right
  AND NOT EXISTS (
        SELECT 1 FROM (<SelectSql>) m
         WHERE m.[L] > @left AND m.[R] < @right      -- m внутри того же поддерева
           AND m.[L] < s.[L] AND m.[R] > s.[R]        -- m — предок s
      )
```

Смысл: если между родителем и `s` есть ещё один узел `m` (охватывающий `s` и сам лежащий в
поддереве родителя), значит `s` — не прямой ребёнок. Это стандартный приём получения прямых
потомков в Nested Set без колонки уровня. `OFFSET/FETCH`, CTE, оконных функций здесь нет —
совместимо с SQL Server 2008 R2.

Ветку **с** `LevelColumn` оставить как есть: заказчик колонку не использует, но код-путь
рабочий и его удаление — отдельное решение (см. «Не делай»).

## Что сделать

Файл `src/Clayzor.Lib.Entities/Tree/ClayTreeSqlBuilder.cs`, метод `BuildNestedSetSql`,
блок `else` (не-корневой уровень). Было:

```csharp
else
{
    sb.Append(" WHERE s.[").Append(src.Schema.LeftColumn).Append("] > @").Append(LeftParam);
    sb.Append(" AND s.[").Append(src.Schema.RightColumn).Append("] < @").Append(RightParam);
    if (src.Schema.LevelColumn is not null)
        sb.Append(" AND s.[").Append(src.Schema.LevelColumn).Append("] = @").Append(LevelParam).Append(" + 1");
}
```

Стало:

```csharp
else
{
    sb.Append(" WHERE s.[").Append(src.Schema.LeftColumn).Append("] > @").Append(LeftParam);
    sb.Append(" AND s.[").Append(src.Schema.RightColumn).Append("] < @").Append(RightParam);
    if (src.Schema.LevelColumn is not null)
    {
        // Есть колонка уровня — прямые дети одним предикатом.
        sb.Append(" AND s.[").Append(src.Schema.LevelColumn).Append("] = @").Append(LevelParam).Append(" + 1");
    }
    else
    {
        // Колонки уровня нет: прямой ребёнок — узел поддерева без промежуточного предка внутри того же поддерева.
        sb.Append(" AND NOT EXISTS (SELECT 1 FROM (").Append(src.SelectSql)
          .Append(") m WHERE m.[").Append(src.Schema.LeftColumn).Append("] > @").Append(LeftParam)
          .Append(" AND m.[").Append(src.Schema.RightColumn).Append("] < @").Append(RightParam)
          .Append(" AND m.[").Append(src.Schema.LeftColumn).Append("] < s.[").Append(src.Schema.LeftColumn)
          .Append("] AND m.[").Append(src.Schema.RightColumn).Append("] > s.[").Append(src.Schema.RightColumn).Append("])");
    }
}
```

`@left`/`@right` уже передаются в `ClayTreeData.BuildParams` (проверено) — новых параметров не
нужно. `LevelParam` в этой ветке по-прежнему не участвует.

## Не делай

- **Не возвращай обязательность `LevelColumn`** и не трогай `ClayTreeSchema.Validate` — решение
  заказчика: колонки нет.
- Не удаляй ветку `if (LevelColumn is not null)` — она рабочая, её удаление вне рамок заплатки.
- Не трогай корневую ветку NestedSet (там свой `NOT EXISTS` для корней — это CTF-независимо;
  корректность корней проверяется отдельно в тестах).
- Не переходи на CTE/оконные функции/`OFFSET`.
- Не меняй `ClayTreeData`, `ClaySqlTreeDataSource`, компонент — это CTF2 и далее.
- Не «оптимизируй» `NOT EXISTS` вынесением подзапроса — билдер отдаёт одну строку SQL, читаемость
  диффа важнее.

## Проверка

**Тест** — в `tests/Clayzor.Lib.Web.Controls.Tests/ClayTreeSqlBuilderTests.cs` добавить:

1. `NestedSet`, не-корневой уровень, **без** `LevelColumn`: сгенерированный SQL содержит
   `NOT EXISTS` и **не** содержит `[Level] =` (или как названа колонка) — прямые дети через
   отсутствие промежуточного предка.
2. `NestedSet`, не-корневой уровень, **с** `LevelColumn`: SQL содержит `= @level + 1` и **не**
   содержит `NOT EXISTS` в этой ветке — старое поведение цело.
3. Все идентификаторы в новом `NOT EXISTS` — в квадратных скобках; значения — только
   `@left`/`@right`, литералов диапазона в тексте нет.

**Ручной прогон** (`/tree-test`, режим «Вложенные множества», схема без `LevelColumn`):
- раскрыть «Оборудование» → **ровно** «Компьютерная техника», «Мебель», «Прочее»; ни одного
  внука;
- раскрыть «Компьютерная техника» → «Ноутбуки», «Мониторы»; не «Ноутбук Dell Latitude» напрямую;
- раскрыть «Ноутбуки» → «Ноутбук Dell Latitude», «Ноутбук HP ProBook»;
- на каждое раскрытие — **один** SQL-запрос (профайлер);
- режим `ParentKey` не затронут — состав и порядок те же.

`dotnet build` + `dotnet test` — зелёные.

> После этой заплатки состав уровней верный, но узлы всё ещё могут рисоваться без отступа —
> это CTF2 (уровень узла). Заплатки независимы, но обе нужны для корректного вида.
