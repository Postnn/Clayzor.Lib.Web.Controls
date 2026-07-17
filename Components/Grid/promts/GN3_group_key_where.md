> Часть плана «Группировка на произвольное число уровней». Перед началом прочитай **GN0_README_grouping_levels.md** и **_readme_grid_dynamic.md**. Требует выполненного **GN2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GN3 — `BuildGroupKeyWhere`: детальный WHERE на N уровней и с `IS NULL`

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGroupingEngine.cs` — `GridGroupAgg.RawKeys` (после GN2 там N уровней,
  включая `null`), `BuildDetailPageSql`.
- **Все места, где собирается детальный `WHERE` по ключам группы. Найди их сам:**
  `grep -rn "dk{i}\|dk0\|@dk" --include=*.cs .` — и глазами по файлам:
  - `Components/Grid/ClayGridPageBase.cs` → `LoadGroupedData`;
  - `Components/Grid/ClayGridPageBase.Export.Print.cs` → `BuildAllGroupedRowsForPrint`;
  - `Components/Grid/ClayGrid.Dynamic.Grouping.cs` → `LoadDynamicGroupedData` (**если есть**, GG2);
  - `Components/Grid/ClayGrid.Dynamic.Export.cs` → `BuildDynamicExportRowsForCurrentPage` (**если есть**, GE3);
  - `Components/Grid/ClayGrid.Dynamic.Grouping.cs` → `LoadDynamicGroupChildIdsAsync` (**если есть**, GG8) —
    у него `WHERE` строится не из `RawKeys`, а из `FullKey.Split('\u001F')`. **Это отдельный
    случай, см. пункт 3.**
- `Components/Grid/ClayDataQuery.cs` — `CombineWhere`.

Файлов `ClayGrid.Dynamic.*` может не быть (планы GG/GE не выполнялись) — тогда пропусти их и
**скажи об этом в отчёте**, не создавай.

## Задача

Во всех местах детальный `WHERE` собирается копипастой одного вида:

```csharp
var keyParts = ag.RawKeys
    .Select((k, i) => { detailParams.Add($"dk{i}", k); return $"{exprs[i]} = @dk{i}"; })
    .ToList();
var detailWhere = ClayDataQuery.CombineWhere(where, string.Join(" AND ", keyParts));
```

Два дефекта.

**1. `= @dk{i}` при `k == null` даёт `КодТипа = NULL`.** В SQL это никогда не истина — группа
«(пусто)» раскроется пустой, хотя счётчик в заголовке показывает «(4 шт.)». До GN2 это не
всплывало: `null`-ключи туда просто не попадали (уровень «исчезал», см. GN0, «Факт 4»). После
GN2 `RawKeys` содержит `null` честно — и дефект стал видимым. Нужен `КодТипа IS NULL`.

**2. Копипаста в 3–5 местах.** Число уровней теперь произвольное, `null` требует особой ветки —
такую логику нельзя держать в пяти экземплярах. Выносим в движок.

Заодно: `dk{i}` — имя параметра без префикса. В `LoadDynamicGroupChildIdsAsync` (GG8) для
уникальности уже используется свой префикс. Делаем префикс параметром.

## Изменить/создать

**1.** `ClayGroupingEngine.cs` — новый метод:

```csharp
    /// <summary>
    /// Строит WHERE-фрагмент, отбирающий строки одной группы по её ключам.
    /// NULL-ключ превращается в <c>IS NULL</c>: <c>col = @p</c> при NULL-значении параметра
    /// не истинно никогда, и группа «(пусто)» раскрывалась бы пустой.
    /// Число уровней не ограничено — берётся из <paramref name="rawKeys"/>.
    /// Значения ключей уходят ПАРАМЕТРАМИ; в текст SQL подставляются только выражения колонок
    /// из <paramref name="groupExprs"/> (они обязаны быть провалидированы вызывающим).
    /// </summary>
    /// <param name="groupExprs">Выражения колонок группировки по уровням.</param>
    /// <param name="rawKeys">Сырые значения ключей группы (<see cref="GridGroupAgg.RawKeys"/>).</param>
    /// <param name="paramPrefix">Префикс имён параметров, напр. "dk". Должен быть уникален в пределах одного набора параметров.</param>
    /// <param name="parameters">Параметры для добавления в DynamicParameters. Для NULL-ключей параметр не создаётся.</param>
    /// <returns>Фрагмент вида <c>a = @dk0 AND b IS NULL</c>. Пустая строка, если ключей нет.</returns>
    public static string BuildGroupKeyWhere(
        IReadOnlyList<string> groupExprs,
        IReadOnlyList<object?> rawKeys,
        string paramPrefix,
        out List<(string Name, object? Value)> parameters)
    {
        parameters = [];
        if (rawKeys.Count == 0) return "";

        var parts = new List<string>(rawKeys.Count);
        for (int i = 0; i < rawKeys.Count && i < groupExprs.Count; i++)
        {
            if (rawKeys[i] is null)
            {
                parts.Add($"{groupExprs[i]} IS NULL");
                continue;
            }

            var name = $"{paramPrefix}{i}";
            parameters.Add((name, rawKeys[i]));
            parts.Add($"{groupExprs[i]} = @{name}");
        }

        return string.Join(" AND ", parts);
    }
```

Почему так:

- **`out List<(string, object?)>`, а не `DynamicParameters`.** `ClayGroupingEngine` не зависит
  от Dapper и не должен начать. Вызывающий сам добавит пары в свой набор.
- **`i < groupExprs.Count`** — защита от рассинхрона `RawKeys` и `groupExprs`. Молча
  ограничиваемся, а не падаем.
- **Пустая строка при `rawKeys.Count == 0`.** Вызывающий обязан это проверить: пустой `WHERE`
  для группы означает «вся таблица». В GG8 это критично — см. пункт 3.
- Имя `@dk0` сохраняем по умолчанию — чтобы диффы читались, а SQL в профайлере выглядел
  привычно.

**2.** Перевести на него ВСЕ места сборки детального `WHERE`. Шаблон замены (пример для
`ClayGridPageBase.LoadGroupedData`, в остальных — так же с поправкой на имена переменных):

```csharp
                var ag           = item.Aggregate;
                var detailParams = new DynamicParameters();
                detailParams.AddDynamicParams(dp);

                var keyWhere = ClayGroupingEngine.BuildGroupKeyWhere(exprs, ag.RawKeys, "dk", out var keyParams);
                foreach (var (name, value) in keyParams)
                    detailParams.Add(name, value);

                var detailWhere = ClayDataQuery.CombineWhere(where, keyWhere);

                detailParams.Add("__start", item.DetailStart);
                detailParams.Add("__end",   item.DetailEnd);
```

`CombineWhere(where, "")` обязан вернуть `where` без хвостового `AND` — **проверь это чтением
`CombineWhere`, а не на веру.** Если он так не умеет, оберни: `keyWhere.Length > 0 ? CombineWhere(where, keyWhere) : where`.

Переменная с колонками группировки называется в разных файлах по-разному (`exprs`, `groupCols`,
`_dynamicGroupExprs`) — подставь фактическое имя.

**3.** `LoadDynamicGroupChildIdsAsync` (GG8) — **особый случай, читай внимательно.** Он строит
`WHERE` не из `RawKeys`, а из `FullKey.Split('\u001F')`, то есть из СТРОК:

```csharp
var keys = fullKey.Split('\u001F');
for (int i = 0; i < keys.Length && i < _dynamicGroupExprs.Count; i++)
{
    var pName = $"gk_{fullKey.GetHashCode() & 0x7FFFFFFF}_{i}";
    dp.Add(pName, keys[i]);
    keyParts.Add($"{_dynamicGroupExprs[i]} = @{pName}");
}
```

После GN2 пустой сегмент в `FullKey` означает `NULL`-ключ, и `col = ''` его не найдёт. Переводи
так:

```csharp
            var keys = fullKey.Split('\u001F');
            // Пустой сегмент FullKey — это NULL-ключ (GN2). Восстанавливаем его как null,
            // чтобы BuildGroupKeyWhere дал IS NULL, а не сравнение с пустой строкой.
            var rawKeys = keys.Select(k => k.Length == 0 ? null : (object?)k).ToList();

            var prefix   = $"gk_{fullKey.GetHashCode() & 0x7FFFFFFF}_";
            var keyWhere = ClayGroupingEngine.BuildGroupKeyWhere(
                _dynamicGroupExprs, rawKeys, prefix, out var keyParams);

            if (keyWhere.Length == 0) continue;   // без условий выбрались бы ID всей таблицы

            var dp = new DynamicParameters();
            dp.AddDynamicParams(_dynamicGroupParams);
            foreach (var (name, value) in keyParams)
                dp.Add(name, value);

            var combinedWhere = ClayDataQuery.CombineWhere(_dynamicGroupWhere, keyWhere);
```

**Известное ограничение, которое ты НЕ чинишь:** ключи здесь — строки (`FullKey` собран из
`ToString()`), исходные типы потеряны, и `col = @gk_..._0` полагается на неявное приведение
SQL Server. Так было до тебя, так и оставь. Отличить «настоящая пустая строка в данных» от
«NULL» здесь невозможно — оба дают пустой сегмент; группа с пустой строкой в ключе получит
`IS NULL` и не найдёт своих строк. Это редкий край; **зафиксируй его в `GF7_backlog.md`**
как отдельную находку с пометкой «лечится передачей `RawKeys` вместо `FullKey`».

## Не делай

Не добавляй ссылку на Dapper в `ClayGroupingEngine` — метод отдаёт пары, а не
`DynamicParameters`. Не подставляй значения ключей в текст SQL — только параметрами. Не трогай
`BuildAggregates` (GN2), `BuildDetailPageSql`, `BuildDetailOrder`, `BuildTree`, `WalkTree`.
Не трогай C#-interleaving экспорта — GN4. Не меняй разделитель `\u001F`. Не пытайся отличить
пустую строку от NULL в `LoadDynamicGroupChildIdsAsync` — занеси в бэклог.

## Проверка

**Юнит (`BuildGroupKeyWhere`) — БД не нужна:**

- `(["a"], ["x"], "dk")` → `"a = @dk0"`, `parameters` = `[("dk0", "x")]`;
- `(["a","b"], ["x","y"], "dk")` → `"a = @dk0 AND b = @dk1"`, два параметра;
- **`(["a","b"], ["x", null], "dk")` → `"a = @dk0 AND b IS NULL"`, параметр ОДИН (`dk0`)** —
  главный тест шага;
- `(["a"], [null], "dk")` → `"a IS NULL"`, `parameters` пуст;
- `(["a","b","c","d","e"], [1,2,3,4,5], "dk")` → пять условий `@dk0..@dk4` — потолка нет;
- `(["a"], [], "dk")` → `""`, `parameters` пуст;
- `(["a"], ["x","y"], "dk")` (ключей больше, чем выражений) → `"a = @dk0"`, без исключения;
- `paramPrefix = "gk_7_"` → имена `@gk_7_0`, `@gk_7_1`;
- значение `0`, `false`, `""` (не null) → идут ПАРАМЕТРОМ, а не `IS NULL`.

**Ручная.** Статический режим (`MedicalTests.razor`), колонка с `NULL` в данных:

- сгруппировать по ней → есть группа «(пусто)» со счётчиком «(N шт.)»;
- **раскрыть её → показаны ровно N строк** (до GN3 было 0). Это приёмка шага;
- в профайлере детальный запрос содержит `... IS NULL`, а не `= @dk0`;
- раскрыть обычную группу → как раньше, `= @dk0`;
- двухуровневая группировка, где `NULL` во внутренней колонке → раскрытие работает на обоих
  уровнях;
- **три и пять уровней** → раскрытие любой группы показывает её строки, детальный `WHERE`
  содержит по одному условию на уровень;
- «Печать → Все данные» и «Печать → Текущая страница» с группировкой → детали групп на месте
  (`BuildAllGroupedRowsForPrint` — одно из переведённых мест).

Динамический режим (если сделаны GG7 / GE6) — те же сценарии, плюс:

- с `GF13` + `GG8`: чекбокс группы «(пусто)» выбирает её записи, а не ноль записей;
- чекбокс группы с обычным ключом — как раньше;
- в профайлере запрос ID потомков содержит `IS NULL` для пустого сегмента.

Регрессия: группировка без единого `NULL` в данных на 1–2 уровнях обязана работать точно как
до GN3, и SQL в профайлере обязан выглядеть так же (`= @dk0 AND ... = @dk1`).
