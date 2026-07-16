> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Требует выполненного **GG3**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG4 — `GroupRowHostKey` должен знать про динамическую колонку редактирования

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGrid.Grouping.cs` — свойство `GroupRowHostKey` и `IsGroupRowHost`.
- `Components/Grid/ClayGrid.razor` — сервисная колонка: условие её рендеринга
  (`@if (EditDialogType is not null || HasDynamicEdit)`), её `CellClassFunc` и `CellTemplate`;
  колонки из `_columnOrder`: их `CellClassFunc` и ветка `@if (IsGroupRowHost(sqlName))`.
- `Components/Grid/ClayGrid.Dynamic.cs` — `HasDynamicEdit`, `HasDynamicDelete`,
  `GetEditColumnStyle`.
- `Kesco.App.Web.Inventory/wwwroot/css/app.css` — правила `.group-header-cell` и
  `.mud-table-row:has(.group-header-cell)`. **Прочитай их: там заголовок группы выводится
  overlay-ом поверх строки (`width: 0; overflow: visible; position: relative`), и от того,
  в какой колонке он живёт, зависит, откуда он начнёт рисоваться.**

## Задача

`GroupRowHostKey` выбирает, в какой колонке рисовать заголовок группы (шеврон + подпись +
счётчик):

```csharp
private string GroupRowHostKey
{
    get
    {
        if (EditDialogType is not null) return "__edit__";
        foreach (var colId in _columnOrder)
        {
            if (!_columnById.TryGetValue(colId, out var meta)) continue;
            if (_hiddenSqlNames.Contains(meta.SqlName)) continue;
            if (IsGrouped(meta.SqlName)) continue;
            return meta.SqlName;
        }
        return "";
    }
}
```

Первое условие проверяет ТОЛЬКО `EditDialogType`. Сервисная колонка при этом рендерится по
более широкому условию — `@if (EditDialogType is not null || HasDynamicEdit)`. В динамическом
режиме `EditDialogType` всегда `null` (диалог редактирования там не используется, вместо него
переход на `ФормаРедактирования`), а `HasDynamicEdit` истинен, когда в `Запросы` заполнено
`ФормаРедактирования`.

Итог для динамического грида с настроенной формой редактирования: сервисная колонка (карандаш,
корзина) есть и стоит первой, а `GroupRowHostKey` вернёт первую колонку ДАННЫХ. Заголовок
группы начнёт рисоваться со второй колонки, оставив слева пустую ячейку шириной 44–88px, и
уедет вправо относительно шевронов статического грида. Плюс `CellClassFunc` сервисной колонки
(`GroupRowHostKey == "__edit__"`) не сработает — класс `group-header-cell` на неё не попадёт,
и CSS-правила overlay-я применятся к другой ячейке.

Ровно та же проверка `EditDialogType is not null` в `ClayGrid.razor` уже расширена до
`|| HasDynamicEdit`. `GroupRowHostKey` про это забыли.

## Изменить/создать

`ClayGrid.Grouping.cs`:

```csharp
    /// <summary>
    /// SqlName колонки, которая должна отображать заголовок группы (шеврон + подпись + счётчик).
    /// "__edit__" — сервисная колонка. Условие должно совпадать с условием рендеринга
    /// сервисной колонки в ClayGrid.razor (EditDialogType is not null || HasDynamicEdit),
    /// иначе заголовок группы уедет в другую колонку, а CellClassFunc сервисной колонки
    /// не проставит group-header-cell.
    /// Никогда не совпадает с колонкой, скрытой текущей группировкой или пользовательскими
    /// настройками — вычисляется заново на каждый рендер.
    /// </summary>
    private string GroupRowHostKey
    {
        get
        {
            if (EditDialogType is not null || HasDynamicEdit) return "__edit__";
            foreach (var colId in _columnOrder)
            {
                if (!_columnById.TryGetValue(colId, out var meta)) continue;
                if (_hiddenSqlNames.Contains(meta.SqlName)) continue;
                if (IsGrouped(meta.SqlName)) continue;
                return meta.SqlName;
            }
            return "";
        }
    }
```

Одна строка. `HasDynamicEdit` объявлен в `ClayGrid.Dynamic.cs`, это тот же partial-класс —
никаких using не нужно.

Проверь по коду `ClayGrid.razor`, что условие рендеринга сервисной колонки — именно
`EditDialogType is not null || HasDynamicEdit`, а не что-то другое. Если оно с тех пор
изменилось — приведи `GroupRowHostKey` к фактическому условию и скажи об этом, не выдумывай.

## Не делай

Не меняй условие рендеринга сервисной колонки в `ClayGrid.razor`. Не трогай CSS
`.group-header-cell` — правила общие для обоих режимов, и статический грид на них живёт.
Не меняй `IsGroupRowHost`. Не включай группировку (`Groupable` остаётся `false`) — это GG7.
Не трогай `_columnOrder` и `_hiddenSqlNames`.

## Проверка

**Ручная (временный хак из GG2/GG3: `_groupColumns.Add("КодТипа");` в конце `InitDynamicMode`).**

Грид 140 в сиде имеет `ФормаРедактирования = /medical/edit` → `HasDynamicEdit == true`:

- `?id=140&CLID=9` → заголовок группы (шеврон + значение + «(N шт.)») начинается с САМОЙ
  ЛЕВОЙ ячейки строки, а не со второй; слева от шеврона пустого места нет;
- строка заголовка группы визуально отличается: подсветка фона и верхняя граница
  (`.mud-table-row:has(.group-header-cell)`), то есть класс проставился;
- у строк детализации карандаш и корзина на месте, у строки заголовка группы их нет;
- временно очистить `ФормаРедактирования` для грида 140 в `Запросы` (`HasDynamicEdit == false`)
  → сервисной колонки нет, заголовок группы рисуется в первой негруппированной колонке
  данных, подсветка строки на месте. Вернуть значение обратно;
- сгруппировать по колонке, которая стоит первой в `_columnOrder` → заголовок группы всё равно
  в сервисной колонке, дыр нет (`IsGrouped` исключает её из кандидатов);
- **убрать временную строку**, пересобрать.

Статический режим (`MedicalTests.razor`, `EditDialogType` задан): заголовок группы там и был
в сервисной колонке — проверь, что ничего не сдвинулось.
