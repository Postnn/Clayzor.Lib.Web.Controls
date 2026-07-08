# G2. Группировка: строка-заголовок на всю ширину (визуальное объединение ячеек)

Часть 2 из 2. Зависит от `G1_group_row_host_column.md` — нужны `GroupRowHostKey` / `IsGroupRowHost`.

Файлы:
- `Components/Grid/ClayGrid.razor` — `CellClassFunc` на колонке чекбокса, колонке редактирования и
  каждой колонке данных в цикле по `_columnOrder`.
- `app.css` (в потребляющем веб-проекте, wwwroot) — расширить существующий блок
  `/* ── Group Header Rows (server-side grouping) ── */`.

**Не трогаем** `Services/ClayGridPrintHtmlGenerator.cs` — у него собственный `<style>`, встроенный в
печатный HTML (`EmbedStyles`), полностью независимый от `app.css`. Печать уже рендерит строку группы
через настоящий `colspan` (`AppendGroupRow`) — это ориентир, как должно выглядеть визуально, а не то, что
нужно чинить.

## Важная находка: инфраструктура уже частично есть

В `app.css` уже есть класс и правило именно под строку-заголовок группы:

```css
/* ── Group Header Rows (server-side grouping) ── */
.mud-table-row:has(.group-header-cell) {
    background-color: rgba(5, 22, 77, 0.04) !important;
    border-top: 2px solid var(--lh-navy) !important;
    cursor: pointer;
}

.mud-table-row:has(.group-header-cell):hover {
    background-color: rgba(5, 22, 77, 0.08) !important;
}

.group-header-cell {
    font-weight: 600;
    color: var(--lh-navy);
}
```

Сегодня класс `.group-header-cell` в интерактивном гриде никогда фактически не навешивается ни на один
`<td>` — поэтому и фон, и жирный шрифт, и верхняя граница у живой группировки не проявляются вовсе
(в отличие от печати, где `ClayGridPrintHtmlGenerator` честно ставит этот класс на `<td colspan="N">`).
Решение — не изобретать новые классы/CSS-хуки, а довести до конца уже начатую идею: через
`CellClassFunc` навесить `group-header-cell` ровно на хост-ячейку (см. G1), а остальные ячейки строки
скрыть тем же селектором `:has()`, которым и так уже стилизуется строка.

`RowClassFunc` не нужен — селектор `:has(.group-header-cell)` уже решает задачу «стилизовать строку по
наличию дочерней ячейки», его нужно только дополнить layout-правилами.

## Правки

### 1. `CellClassFunc` в `ClayGrid.razor`

На колонке чекбокса (блок `_selectMode`) — класс-«пин», чтобы её не схлопнуло вместе с остальными:

```razor
<TemplateColumn @key="SelectColumnKey" T="TEntity"
                Title=""
                Sortable="false"
                DragAndDropEnabled="false"
                CellClassFunc="@(item => item is GroupHeaderRow ? "clay-group-pin-cell" : "")"
                Style="width:48px;min-width:48px;max-width:48px">
```

На колонке редактирования:

```razor
<TemplateColumn @key="ServiceEditColumnKey" T="TEntity"
                Title=""
                Sortable="false"
                DragAndDropEnabled="false"
                CellClassFunc="@(item => item is GroupHeaderRow && GroupRowHostKey == "__edit__" ? "group-header-cell" : "")"
                Style="width:44px;min-width:44px;max-width:44px">
```

На каждой колонке данных в `@foreach (var colId in _columnOrder)`:

```razor
<TemplateColumn @key="colId" T="TEntity"
                Title="@dispName"
                Sortable="false"
                Hidden="@IsGrouped(sqlName)"
                DragAndDropEnabled="false"
                CellClassFunc="@(item => item is GroupHeaderRow && IsGroupRowHost(sqlName) ? "group-header-cell" : "")">
```

Больше никаких новых C#-методов не нужно — `GroupRowHostKey`/`IsGroupRowHost` уже введены в G1.

### 2. Дополнить блок в `app.css`

Дописать после существующего блока `/* ── Group Header Rows ── */` (не трогая то, что там уже есть):

```css
/* ── Group Header Rows: полная ширина в интерактивном гриде ──
   TemplateColumn не даёт colspan на уровне строки (в отличие от печати —
   см. ClayGridPrintHtmlGenerator.AppendGroupRow, там честный colspan).
   Эмулируем полосу на всю ширину: превращаем строку во flex-контейнер,
   схлопываем все ячейки, кроме хост-ячейки (.group-header-cell) и
   запиненной колонки чекбокса (.clay-group-pin-cell). */
.mud-table-row:has(.group-header-cell) {
    display: flex;
    align-items: stretch;
}

.mud-table-row:has(.group-header-cell) > .mud-table-cell:not(.group-header-cell):not(.clay-group-pin-cell) {
    display: none !important;
}

.mud-table-row:has(.group-header-cell) .group-header-cell {
    flex: 1 1 auto !important;
    width: auto !important;
    min-width: 0 !important;
    max-width: none !important;
    display: flex !important;
    align-items: center;
    border-right: none !important;
}

.mud-table-row:has(.group-header-cell) .clay-group-pin-cell {
    flex: 0 0 auto;
}
```

Используются компаунд-селекторы (`.mud-table-row:has(...) .group-header-cell`, а не голый
`.group-header-cell`) специально для того, чтобы не зависеть от порядка правил в файле относительно
общих `.mud-table-cell { border-right: 1px solid ... !important }` — по чистой специфичности новые
правила гарантированно победят вне зависимости от того, в каком месте файла их дописать.

Ручной отступ на уровень вложенности не трогаем — он уже есть в `ClayGroupHeader.razor`
(`Style="padding-left:{Header.Depth * 16}px"`) и никогда не был завязан на колонки.

## Критерии

- [ ] Строка-заголовок группы визуально тянется на всю ширину таблицы (сравнить с тем, как это уже
      выглядит в печатной форме — `ClayGridPrintHtmlGenerator`).
- [ ] Между чекбоксом (если включён режим выбора) и полосой заголовка группы нет «дыр» — прочие ячейки
      для строки-заголовка не видны и не оставляют пустого места.
- [ ] Сценарий скриншота 1 (COVID-19 / колонка редактирования как хост) — пустая ячейка редактирования
      слева от заголовка больше не выглядит отдельной колонкой, надпись растянута на всю ширину.
- [ ] Сценарий скриншота 2 (группировка по «Тип» и «Код») — строки-заголовки не пустые, видны на всех
      уровнях вложенности.
- [ ] Многоуровневая группировка (2+ уровня) — каждый уровень отступает вручную (`Depth * 16px`) и не
      залезает на соседние ячейки при разворачивании/сворачивании.
- [ ] Обычные строки данных (`DetailRow<T>`) выглядят как раньше — `CellClassFunc` возвращает `""`,
      новые правила `app.css` их не задевают (они все под `:has(.group-header-cell)`).
- [ ] Печатная форма (`ClayGridPrintHtmlGenerator`) не изменилась внешне — её стили независимы.
- [ ] `dotnet build` без ошибок; визуальная проверка в браузере (обычная и суженная ширина окна,
      горизонтальный скролл грида).
