> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Требует выполненного **GG2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG6 — заголовок группы для Тип 5 / Тип 9: наименование, а не код

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGrid.Dynamic.cs` — `_dynamicLookups` (что в ключе, что в значении),
  `_dynamicIconLookups`, блоки их загрузки в `InitDynamicMode` (Тип 5 через
  `DynamicSql.QueryPairsAsync`, Тип 9 через `QueryTriplesAsync`), сборка cell-шаблона —
  **как именно в ячейке код заменяется на наименование**.
- `Components/Grid/Dynamic/ClayColumnKind.cs` — значения `List` (5) и `Icon` (9).
- `Components/Grid/ClayGroupingEngine.cs` — `GridGroupAgg.DisplayValue`, `KeyValues`, `RawKeys`;
  `WalkTree` — откуда `GroupHeaderRow.DisplayValue` берёт значение.
- `Components/Grid/ClayGroupHeader.razor` — что рендерится (`@Header.DisplayValue (@Header.ItemCount шт.)`).
- `Components/Grid/ClayGrid.Dynamic.Grouping.cs` (GG2) — `LoadDynamicGroupedData`.
- `scripts/dynamic-grid/schema.sql` — колонка 1004: `Колонка = КодТипа`, `Тип = 5`,
  `Формат = SELECT КодТипа, Наименование FROM Типы ORDER BY Наименование`.

## Задача

Колонка Тип 5 (Список) хранит в данных КОД, а показывает НАИМЕНОВАНИЕ из справочника: SQL
справочника лежит в `ЗапросыКолонки.Формат`, `InitDynamicMode` выполняет его один раз и кладёт
результат в `_dynamicLookups[sqlName]` как `код → наименование`. Cell-шаблон подменяет значение
при рендеринге ячейки.

Группировка идёт по колонке в SQL, то есть по КОДУ — это правильно и менять не нужно.
Но `GroupHeaderRow.DisplayValue` заполняет `ClayGroupingEngine.WalkTree` из
`node.Aggregate.DisplayValue`, а тот приходит из `BuildAggregates` как `keys.Last()`, то есть
`gr.K0.ToString()`. Справочника движок не знает и знать не должен.

Итог: сгруппировав по «Тип исследования», пользователь увидит заголовки групп «3 (12 шт.)»,
«7 (5 шт.)» вместо «Общий анализ крови (12 шт.)», «Биохимия (5 шт.)». В ячейках при этом
наименования — то есть грид сам себе противоречит.

То же для Тип 9 (Пиктограмма): `_dynamicIconLookups[sqlName]` — это `код → (Tooltip, Href)`.
В заголовке группы картинку не покажешь (`ClayGroupHeader` выводит текст), поэтому используем
`Tooltip` — это человекочитаемая подпись значения.

## Изменить/создать

`ClayGrid.Dynamic.Grouping.cs`.

**1.** Функция подстановки наименования:

```csharp
    /// <summary>
    /// Отображаемое значение группы. Группировка идёт по коду (значению колонки в SQL),
    /// а показывать нужно наименование — как в ячейке.
    /// Тип 5 (Список): _dynamicLookups[колонка][код] → наименование.
    /// Тип 9 (Пиктограмма): _dynamicIconLookups[колонка][код].Tooltip — картинку в текстовом
    /// заголовке группы не показать, тултип это человекочитаемая подпись значения.
    /// Кода нет в справочнике → возвращаем код как есть (так же ведёт себя cell-шаблон).
    /// </summary>
    private string ResolveGroupDisplayValue(string groupSqlName, string rawValue)
    {
        if (_dynamicLookups.TryGetValue(groupSqlName, out var lookup)
            && lookup.TryGetValue(rawValue, out var text))
            return text;

        if (_dynamicIconLookups.TryGetValue(groupSqlName, out var iconLookup)
            && iconLookup.TryGetValue(rawValue, out var iconData))
            return iconData.Tooltip;

        return rawValue;
    }
```

**2.** Применить после `WalkTree`, до сборки строк. В `LoadDynamicGroupedData`, сразу после
вызова `ClayGroupingEngine.WalkTree(...)`:

```csharp
        ClayGroupingEngine.WalkTree(roots, query.ExpandedGroups, pageStart, pageEnd, ref cur, layout);

        // Подмена кода на наименование в заголовках групп (Тип 5/9).
        // Только DisplayValue: FullKey, KeyValues и RawKeys обязаны остаться кодами —
        // на них построены ExpandedGroups и WHERE детального запроса.
        foreach (var item in layout)
        {
            if (item.Header is null) continue;
            var depth = item.Header.Depth;
            if (depth < 0 || depth >= exprs.Count) continue;
            item.Header.DisplayValue = ResolveGroupDisplayValue(exprs[depth], item.Header.DisplayValue);
        }
```

**Самое важное в этом шаге — что НЕЛЬЗЯ трогать:**

- `GroupHeaderRow.FullKey` — по нему живёт `_dynamicExpandedGroups` (GG3). Подмени его на
  наименование — раскрытие групп развалится, а при совпадающих наименованиях разных кодов
  группы начнут раскрываться парами;
- `GridGroupAgg.RawKeys` — они уходят параметрами `@dk0`/`@dk1` в WHERE детального запроса.
  Наименование там не найдёт ничего, группа будет вечно пустой;
- `GridGroupAgg.KeyValues` и `DisplayValue` — их править не нужно, правим только
  `GroupHeaderRow.DisplayValue`, то есть то, что реально рендерится;
- `ClayGroupingEngine` — ни строчки. Справочники — это уровень динамического режима, движок
  про них не знает и знать не должен.

**Про `Depth` и `exprs[depth]`:** заголовок группы на глубине 0 соответствует первой колонке
группировки (`exprs[0]`), на глубине 1 — второй. Проверка `depth < exprs.Count` — защита от
рассинхрона; молча пропускаем, а не падаем.

**Про `_dynamicLookups` для скрытых колонок:** после GF4 справочники грузятся для ВСЕХ колонок
вывода (`gridCols`), включая скрытые по умолчанию. Если ты видишь в коде, что справочники
грузятся только для видимых — значит GF4 не выполнен, ОСТАНОВИСЬ и скажи об этом.

## Не делай

Не меняй `ClayGroupingEngine`. Не подменяй `FullKey`, `RawKeys`, `KeyValues`. Не грузи
справочник заново в `LoadDynamicGroupedData` — он загружен один раз в `InitDynamicMode`,
повторный запрос на каждую загрузку данных недопустим. Не показывай `<img>` в заголовке
группы для Тип 9 — `ClayGroupHeader` текстовый, менять его (общий для обоих режимов) в этом
шаге нельзя. Не включай группировку (`Groupable` остаётся `false`) — это GG7.

## Проверка

**Ручная (временный хак: `_groupColumns.Add("КодТипа");` в конце `InitDynamicMode`).**

`?id=140&CLID=9` (колонка `КодТипа` — Тип 5 со справочником `Типы`):

- заголовки групп показывают НАИМЕНОВАНИЯ («Общий анализ крови (12 шт.)»), а не коды («3 (12 шт.)»);
- значения в ячейках колонки совпадают с подписями групп;
- раскрыть группу → внутри именно записи этого типа, а не пусто (`RawKeys` остались кодами);
- раскрыть две группы, перейти по страницам → раскрытые остались раскрытыми (`FullKey` — код);
- добавить в `Типы` запись, на которую никто не ссылается → лишней группы нет;
- поставить в данных `КодТипа`, которого нет в `Типы` → группа показывает код как есть,
  грид не падает;
- временно сменить `_groupColumns.Add("КодТипа")` на колонку Тип 2 (`Название`, без справочника)
  → заголовок группы показывает само значение, `ResolveGroupDisplayValue` вернул `rawValue`;
- профайлер: справочник `SELECT КодТипа, Наименование FROM Типы` выполняется ОДИН раз при
  инициализации, а не на каждое раскрытие группы;
- **убрать временную строку**, пересобрать.

Статический режим (`MedicalTests.razor`): не затронут — `LoadGroupedData` и `ClayGroupingEngine`
не менялись.
