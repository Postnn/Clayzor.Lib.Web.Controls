> Часть плана «Группировка на произвольное число уровней». Перед началом прочитай **GN0_README_grouping_levels.md** (особенно «Факт 2» и «Факт 4») и **_readme_grid_dynamic.md**. Требует выполненного **GN1**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GN2 — `BuildAggregates` на произвольное число уровней

**Ядро плана.** Здесь дублирующиеся заголовки групп и пересекающаяся детализация (GN0, «Факт 2»)
исчезают, а `NULL` в группировочной колонке перестаёт ронять грид (GN0, «Факт 4»).

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGroupingEngine.cs` — `BuildAggregates` (с временным мостом из GN1),
  `GridGroupAgg` (все поля!), `BuildTree`, `ComputeParentCounts`. **Прочитай `BuildTree`
  внимательно: он требует, чтобы родитель в списке предшествовал ребёнку, и это ограничение
  ты обязан сохранить.**
- `Components/Grid/ClayGroupRowMapper.cs` (GN1) — что именно лежит в `GridGroupRow.Keys`.
- `Components/Grid/ClayGridRow.cs` — `GroupHeaderRow.DisplayValue`, `GroupKeys`, `Depth`.
- `Components/Grid/ClayGridPageBase.Export.Excel.cs` — `BuildAllGroupedRowsForExcel`,
  C#-interleaving. **Смотри, как ТАМ строится `FullKey` и `DisplayValue`** — GN2 обязан дать
  тот же формат, иначе `countLookup` в экспорте перестанет находить группы. Сам interleaving
  не трогай, это GN4.

## Задача

Текущий код (после моста GN1):

```csharp
var k0 = gr.Keys.Count > 0 ? gr.Keys[0] : null;
var k1 = gr.Keys.Count > 1 ? gr.Keys[1] : null;
if (k0 is not null) keys.Add(k0.ToString()!);
if (k1 is not null) keys.Add(k1.ToString()!);

var depth = keys.Count - 1;
var rawKeyValues = new object?[] { k0, k1 }.Take(keys.Count).ToList();
```

Две болезни:

1. **Уровней ровно два.** Третий и далее не читаются → строки агрегата, отличающиеся только
   третьим ключом, схлопываются в один `FullKey` → дубли листьев (GN0, «Факт 2»).
2. **Число уровней выводится из `is not null`.** `NULL` в данных = «уровня нет». Одна колонка
   с `NULL` → `keys` пуст → `depth = -1` → `keys.Last()` → `InvalidOperationException`
   (GN0, «Факт 4»).

Обе лечатся одним решением: **уровней ровно `gr.Keys.Count`, всегда; `null` — законное значение
ключа.**

## Изменить/создать

`ClayGroupingEngine.cs`, `BuildAggregates` целиком:

```csharp
    /// <summary>Отображаемое значение группы, когда ключ уровня равен NULL.</summary>
    public const string EmptyGroupDisplay = "(пусто)";

    /// <summary>
    /// Превращает плоские строки GROUP BY в список <see cref="GridGroupAgg"/>
    /// с синтетическими родительскими узлами для промежуточных уровней.
    /// Число уровней берётся из <see cref="GridGroupRow.Keys"/> и не ограничено.
    /// Порядок агрегатов из БД сохраняется (не пересортировывается): <see cref="BuildTree"/>
    /// требует, чтобы родитель предшествовал ребёнку.
    /// </summary>
    public static List<GridGroupAgg> BuildAggregates(IEnumerable<GridGroupRow> groupRows)
    {
        var aggregates = new List<GridGroupAgg>();
        var seenKeys   = new HashSet<string>();

        foreach (var gr in groupRows)
        {
            if (gr.Keys.Count == 0) continue;   // защита от пустой строки агрегата

            // Строковые представления ключей ВСЕХ уровней. null → "" (законное значение).
            var keys  = gr.Keys.Select(k => k?.ToString() ?? "").ToList();
            var depth = keys.Count - 1;

            // Синтетические родители для всех промежуточных уровней 0..depth-1
            for (int d = 0; d < depth; d++)
            {
                var parentKeys    = keys.Take(d + 1).ToList();
                var parentFullKey = string.Join("\u001F", parentKeys);
                if (!seenKeys.Add(parentFullKey)) continue;

                aggregates.Add(new GridGroupAgg
                {
                    FullKey      = parentFullKey,
                    DisplayValue = ToDisplay(parentKeys[d]),
                    ItemCount    = 0,                       // посчитает ComputeParentCounts
                    Depth        = d,
                    ParentKey    = d > 0 ? string.Join("\u001F", parentKeys.Take(d)) : "",
                    KeyValues    = parentKeys,
                    RawKeys      = gr.Keys.Take(d + 1).ToList(),
                });
            }

            var fullKey   = string.Join("\u001F", keys);
            var parentKey = depth > 0 ? string.Join("\u001F", keys.Take(depth)) : "";

            aggregates.Add(new GridGroupAgg
            {
                FullKey      = fullKey,
                DisplayValue = ToDisplay(keys[depth]),
                ItemCount    = gr.Cnt,
                Depth        = depth,
                ParentKey    = parentKey,
                KeyValues    = keys,
                RawKeys      = gr.Keys.ToList(),
            });
        }

        return aggregates;
    }

    /// <summary>Строковый ключ → подпись группы. Пустой ключ (NULL в данных) → «(пусто)».</summary>
    private static string ToDisplay(string key) => key.Length > 0 ? key : EmptyGroupDisplay;
```

Разбор каждого решения — **прочитай, прежде чем что-то менять**:

- **`depth = keys.Count - 1`, а не «сколько ключей не null».** Это и есть исправление обоих
  дефектов. Строка агрегата всегда описывает ЛИСТОВУЮ группу самого глубокого уровня —
  `GROUP BY` по N колонкам иначе строк не порождает.
- **`RawKeys = gr.Keys.ToList()`** — все уровни, сырые значения, включая `null`. Раньше их было
  два, и `null` в них не попадал вовсе (уровень «исчезал»). Теперь `null` в `RawKeys` — норма,
  и детальный `WHERE` обязан превращать его в `IS NULL`. **Этого ещё нет — это GN3.**
  На GN2 группы с `NULL`-ключом будут пустыми при раскрытии; так и должно быть, не чини здесь.
- **`RawKeys` у синтетических родителей теперь заполнены** (`gr.Keys.Take(d + 1)`), а раньше
  были `[]`. Родителю они не нужны для детализации (у него есть дети), но нужны в GG8 для
  выборки ID потомков группы: `LoadDynamicGroupChildIdsAsync` строит `WHERE` по ключам
  ЛЮБОГО узла, включая родительский. Пустой `RawKeys` там означал «условий нет» → выбрались
  бы ID всей таблицы. Заполняем.
- **`ToDisplay`**: `""` → `"(пусто)"`. Формат совпадает с C#-interleaving в экспорте
  (`keys[depth] ?? "(пусто)"`) — иначе подписи групп в гриде и в выгрузке разойдутся.
  Константа `EmptyGroupDisplay` публичная: GN4 будет ссылаться на неё, а не дублировать литерал.
- **`FullKey` для `null` содержит пустой сегмент**: `"a1\u001F"`. Так же ведёт себя
  `string.Join` в interleaving (`string?` null → пустая строка). Совпадение форматов
  обязательно: по `FullKey` живут `ExpandedGroups` и `countLookup` экспорта.
- **`seenKeys` защищает только синтетических родителей** — как и раньше. Листья `GROUP BY`
  уникальны по определению, дублей среди них больше нет: именно их порождал старый код,
  теряя третий ключ.
- **`if (gr.Keys.Count == 0) continue;`** вместо падения на `keys.Last()`. `BuildGroupAggregateSql`
  такого не породит (он бросает `ArgumentException` на пустом `groupExprs`), но `BuildAggregates` —
  публичный метод, дешевле не падать.
- **`keys.Last()` заменён на `keys[depth]`** — то же значение, но явно и без LINQ на горячем пути.

Мост из GN1 и комментарий «НЕ ЧИНИТЬ ЗДЕСЬ» удали — они своё отслужили.

## Не делай

Не трогай `BuildTree`, `ComputeParentCounts`, `ComputeEffectiveRows`, `WalkTree` — **они уже
работают на N уровней**, там нет ни числа 3, ни K-полей (GN0, «Что НЕ трогаем»). Не трогай
`BuildGroupAggregateSql` и `ClayGroupRowMapper` — сделано в GN1. Не трогай сборку детального
`WHERE` — GN3. Не трогай C#-interleaving в `ClayGridPageBase.Export.*` и `ClayGrid.Dynamic.Export` —
GN4. Не пересортировывай `aggregates`. Не меняй разделитель `\u001F`. Не вводи лимит уровней.
Не меняй `GridGroupAgg`.

## Проверка

**Юнит (`BuildAggregates`) — здесь основная ценность шага.** Строй `GridGroupRow` напрямую:
`new GridGroupRow { Keys = [...], Cnt = n }`.

Один уровень:

- `[{Keys=["Кровь"], Cnt=5}, {Keys=["Моча"], Cnt=3}]` → 2 агрегата, оба `Depth == 0`,
  `ParentKey == ""`, `FullKey` = `"Кровь"` / `"Моча"`, `ItemCount` 5 и 3;
- `RawKeys` первого = `["Кровь"]`.

Два уровня (регрессия — должно быть как до GN2):

- `[{["a","b1"],2}, {["a","b2"],3}, {["c","b3"],1}]` → синтетические `"a"` и `"c"` (`Depth 0`,
  `ItemCount 0`), листья `"a\u001Fb1"`, `"a\u001Fb2"`, `"c\u001Fb3"` (`Depth 1`);
- порядок в списке: `"a"` **до** `"a\u001Fb1"` (требование `BuildTree`);
- после `BuildTree` + `ComputeParentCounts`: у `"a"` `ItemCount == 5`, у `"c"` — 1; корней 2.

**Три уровня — главный тест шага (раньше давал дубли):**

- `[{["a","b","c1"],2}, {["a","b","c2"],3}, {["a","b2","c3"],1}]`;
- синтетические: `"a"` (Depth 0), `"a\u001Fb"` (Depth 1), `"a\u001Fb2"` (Depth 1);
- листья: `"a\u001Fb\u001Fc1"` (2), `"a\u001Fb\u001Fc2"` (3), `"a\u001Fb2\u001Fc3"` (1),
  все `Depth == 2`;
- **`aggregates.Select(x => x.FullKey)` не содержит дубликатов** — прямая проверка на дефект
  из GN0, «Факт 2»;
- `BuildTree` → 1 корень `"a"`; у него 2 ребёнка (`b`, `b2`); у `b` — 2 ребёнка;
- `ComputeParentCounts` → `"a"` = 6, `"a\u001Fb"` = 5, `"a\u001Fb2"` = 1.

**Пять уровней:**

- `Keys` из пяти элементов → `Depth == 4`, синтетических родителей 4, `KeyValues.Count == 5`,
  `RawKeys.Count == 5`, `FullKey` из пяти сегментов через `\u001F`;
- `BuildTree` строит цепочку глубиной 5, `ComputeParentCounts` доносит счётчик до корня.

**NULL — второй главный тест:**

- один уровень, `[{Keys=[null], Cnt=4}]` → **исключения НЕТ** (раньше `InvalidOperationException`),
  `FullKey == ""`, `DisplayValue == "(пусто)"`, `Depth == 0`, `RawKeys == [null]`;
- два уровня, `[{["a", null], 2}, {["a","b"], 3}]` → синтетический `"a"` ОДИН,
  листья `"a\u001F"` (Depth 1, DisplayValue `"(пусто)"`) и `"a\u001Fb"` (Depth 1);
  **лист с NULL не всплыл в корень** (раньше становился `Depth 0` и конфликтовал с родителем);
- `BuildTree` → 1 корень, 2 ребёнка; `ComputeParentCounts` → `"a"` = 5;
- `null` на промежуточном уровне: `[{[null,"b"],1}]` → синтетический `""` (Depth 0,
  `"(пусто)"`), лист `"\u001Fb"` (Depth 1);
- `Keys = []` → агрегат пропущен, исключения нет.

**Ручная.** Статический режим (`MedicalTests.razor`):

- одна и две колонки группировки → как до GN2 (регрессии нет);
- **три колонки** → трёхуровневое дерево: каждая подгруппа встречается ОДИН раз, счётчик
  родителя = сумме детей, раскрытие показывает каждую строку ровно один раз и не теряет строк.
  **Пересчитай руками на маленькой выборке** — это тот самый сценарий из GN0, «Факт 2»;
- **четыре и пять колонок** → работает, отступы заголовков растут по `Depth * 16px`;
- колонка, где в данных есть `NULL` → группа «(пусто)» присутствует, грид не падает.
  **Раскрытие такой группы покажет 0 строк — это ожидаемо, чинится в GN3.** Зафиксируй в отчёте;
- пагинация и «Всего: N» при 3+ уровнях считаются верно (`ComputeEffectiveRows`, `WalkTree`
  не менялись).

Динамический режим (если сделан GG7) — те же сценарии.

Печать и Excel с группировкой (GE / статика): дерево на 3+ уровнях в гриде верное, а вот в
экспорте заголовки пока строит C#-interleaving на своей логике — **расхождение возможно и
ожидаемо, лечится в GN4.** Зафиксируй, не чини здесь.
