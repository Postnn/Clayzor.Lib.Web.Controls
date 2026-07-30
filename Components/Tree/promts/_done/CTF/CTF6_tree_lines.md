# CTF6 — направляющие линии дерева (guide lines), аккуратный вариант

> Заплатка к `ClayTreeView`. Стиль — `/AGENTS.md`
> (**Think → Simplicity → Surgical → Goal-Driven**). Пользовательский текст — на русском.
> Git — только по указанию. Один заход. Затрагивает `ClayTreeNodeView.razor`,
> `clay.css`, `ClayTreeOptions.cs`.

## Задача

Добавить направляющие линии иерархии (как в дереве файлов IDE): вертикали по уровням вложенности
и горизонтальный «ус» к строке узла, причём **у последнего ребёнка группы вертикаль обрывается**
на середине строки (не тянется ниже). Включается опцией `ClayTreeOptions.ShowLines`.

**Важно про MudBlazor:** у `MudTreeView` направляющих линий **нет** — это открытый запрос в их
репозитории, не готовое свойство. И включать было бы негде: у `ClayTreeView` своя разметка узла
(`ClayTreeNodeView`), а не обёртка над `MudTreeViewItem` (решение 12 CT1). Поэтому линии делаются
**своим CSS** в `clay.css`, без зависимости от MudBlazor.

## Почему нужна правка разметки, а не только CSS

Текущая структура `ClayTreeNodeView.razor` — плоская: отступ задаётся `padding-left` на строке,
дети лежат прямо в `clay-tree-node` без обёртки:

```razor
<div class="clay-tree-node">
    <div class="clay-tree-node-row" style="padding-left:@(Node.Level * Tree.IndentPx)px"> ... </div>
    @if (Node.IsExpanded && Node.Children.Count > 0)
    {
        @foreach (var child in Node.Children)
        {
            <ClayTreeNodeView Node="child" ... />
        }
    }
</div>
```

Аккуратные линии на **несколько** уровней глубины через один `::before` на строке нарисовать
нельзя — `padding-left` не даёт «столбиков» на каждый промежуточный уровень. Нужна **вложенная**
структура: контейнер детей сдвинут на один `IndentPx` и несёт вертикальную линию своего уровня;
тогда сдвиг набирается вложенностью, а каждый уровень рисует свою вертикаль. Это стандартный
приём CSS-деревьев и он же даёт бесплатный `:last-child` для обрыва.

## Что сделать

### 1. Опция

`Components/Tree/ClayTreeOptions.cs`, блок «Внешний вид»:

```csharp
/// <summary>Показывать направляющие линии иерархии (вертикали по уровням + ус к узлу).</summary>
public bool ShowLines { get; set; } = false;
```

Дефолт `false` — включённые линии меняют вид существующего дерева, а заплатка не должна менять
поведение молча. Добавить строку в защёлку дефолтов `ClayTreeOptionsTests`
(`ShowLines == false`).

### 2. Разметка — перейти на вложенный отступ

`ClayTreeNodeView.razor`. Заменить плоский `padding-left` на вложенный контейнер детей.

- со строки узла **убрать** `style="padding-left:..."`;
- детей завернуть в контейнер `clay-tree-children`, который несёт сам сдвиг и вертикаль уровня;
- признак «последний ребёнок» и «включены линии» пробросить классами.

```razor
@{
    var linesOn = Tree is ClayTreeView { Options.ShowLines: true };
    var isLast  = Node.Parent is null || Node.Parent.Children.Count == 0
                  || ReferenceEquals(Node.Parent.Children[^1], Node);
    var nodeClass = "clay-tree-node"
        + (linesOn ? " clay-tree-node--lines" : "")
        + (linesOn && isLast ? " clay-tree-node--last" : "");
}

<div class="@nodeClass">
    <div class="clay-tree-node-row" style="@(linesOn ? null : $"padding-left:{Node.Level * Tree.IndentPx}px")">
        ... шеврон/спиннер/распорка и текст без изменений ...
    </div>

    @if (Node.IsExpanded && Node.Children.Count > 0)
    {
        <div class="@(linesOn ? "clay-tree-children clay-tree-children--lines" : "clay-tree-children")">
            @foreach (var child in Node.Children)
            {
                <ClayTreeNodeView Node="child" Tree="Tree" NodeTemplate="NodeTemplate" />
            }
        </div>
    }
</div>
```

**Двухрежимность отступа принципиальна для Surgical:** при `ShowLines=false` сохраняется
**прежний** механизм (`padding-left` по `Node.Level`), дерево выглядит ровно как сейчас — ноль
регрессии для тех, кто линии не включал. Отступ через вложенный `clay-tree-children` работает
**только** когда линии включены.

`isLast` считается из модели (`Parent.Children[^1]`), флаг `IsLast` в `ClayTreeNode` **не
добавляется** — данные уже позволяют. `Parent` у корней `null` → корень считается «последним»
для целей обрыва (у корневого уровня вертикали предков нет, так что это безвредно).

### 3. Стили — `wwwroot/css/clay.css`

Рядом с прочими `clay-tree-*`. Линии — через `--mud-palette-lines-default` (штатная палитра
разделителей MudBlazor; если её нет в теме — `--mud-palette-divider`). Своих hex не вводить.

```css
/* Контейнер детей несёт отступ уровня и вертикальную линию слева */
.clay-tree-children--lines {
    margin-left: 11px;               /* центр слота шеврона (22px / 2) */
    padding-left: 11px;
    position: relative;
}

/* Вертикаль уровня: тянется на всю высоту контейнера детей */
.clay-tree-children--lines::before {
    content: "";
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: 1px;
    background: var(--mud-palette-lines-default, var(--mud-palette-divider));
}

/* Горизонтальный ус от вертикали к строке узла */
.clay-tree-node--lines > .clay-tree-node-row {
    position: relative;
}
.clay-tree-node--lines > .clay-tree-node-row::before {
    content: "";
    position: absolute;
    left: -11px;                     /* до вертикали родительского контейнера */
    top: 50%;
    width: 11px;
    height: 1px;
    background: var(--mud-palette-lines-default, var(--mud-palette-divider));
}

/* Обрыв у последнего ребёнка: вертикаль его контейнера-родителя не должна идти ниже его строки.
   Достигается тем, что вертикаль рисует контейнер детей, а у последнего узла
   верхнюю половину «дорисовывает» его собственный ус, нижняя часть отсутствует. */
.clay-tree-node--lines.clay-tree-node--last > .clay-tree-node-row::after {
    content: "";
    position: absolute;
    left: -11px;
    top: 0;
    height: 50%;                     /* только до середины — ниже линии нет */
    width: 1px;
    background: var(--mud-palette-background, var(--mud-palette-surface));
}
```

Механика обрыва: вертикаль непрерывно рисует `clay-tree-children--lines::before` по всей высоте
группы. У **последнего** узла нижняя половина его строки не должна иметь вертикали — поэтому
`--last::after` перекрывает верхний отрезок цветом фона до середины, а горизонтальный ус
(`row::before`) остаётся на середине. Итог: к последнему узлу линия подходит и обрывается, ниже
пусто — как в IDE.

**Проверить фон перекрытия.** `::after` маскирует линию цветом фона дерева. Если под деревом не
`--mud-palette-background`/`surface`, а другой фон (карточка, панель) — подобрать переменную,
совпадающую с фактическим фоном строки. В тёмной теме проверить отдельно: маска не должна
оставлять «полупрозрачный пенёк».

Если механика перекрытия на твоём фоне капризничает — **альтернатива без маски**: у последнего
узла вертикаль не рисуется контейнером, а рисуется псевдоэлементом самого узла от верха до
середины. Но это сложнее и трогает больше правил; маску пробуй первой.

## Ловушки

- **`--last` считается только среди загруженных детей.** Если уровень подгружается лениво,
  `Children` полон к моменту рендера (загрузка завершается до показа), так что `[^1]` корректен.
  Убедиться, что рендер детей идёт после `IsLoaded`, а не во время `IsLoading` (сейчас так:
  дети рисуются `@if (IsExpanded && Children.Count > 0)`).
- **Ширина слота шеврона = 22px** — все магические `11px` в CSS суть половина от 22.
  Если `IndentPx` из опций отличается от 22 — линии всё равно привязаны к слоту шеврона (22),
  а не к `IndentPx`. Это правильно: линия идёт по центру шеврона. `IndentPx` при `ShowLines=true`
  фактически не используется (отступ задаёт `clay-tree-children`). Отметить в отчёте; менять
  семантику `IndentPx` не нужно.
- **`NodeTemplate`.** Ус и вертикали привязаны к `clay-tree-node-row`, а не к тексту, поэтому
  пользовательский шаблон узла линии не ломает. Проверить с заданным `NodeTemplate`.
- **StyleGuard.** Все свойства в CSS — структурные (`position`, `width`, `height`, `margin`,
  `background` через `--mud-palette-*`). Своих цветов нет. Inline `padding-left` на строке
  остаётся только в ветке `ShowLines=false` — он и раньше там был.

## Не делай

- Не добавляй `IsLast` в `ClayTreeNode` — вычисляется из `Parent.Children`.
- Не трогай логику загрузки/раскрытия, спиннер узла (CTF5), выделение.
- Не меняй отступ для случая `ShowLines=false` — там прежний `padding-left`, ноль регрессии.
- Не вводи свои цвета линий — только `--mud-palette-lines-default`/`divider`.
- Не переходи на SVG/canvas — только CSS-псевдоэлементы.
- Не делай линии всегда включёнными — дефолт `ShowLines=false`.

## Проверка

- `dotnet build` + `dotnet test` — зелёные; защёлка `ClayTreeOptionsTests` знает про
  `ShowLines == false`;
- `/tree-test`, `Options.ShowLines = false` (дефолт) → дерево выглядит **как до заплатки**,
  отступ прежний, линий нет;
- `Options.ShowLines = true`, оба режима иерархии:
  - вертикали идут по уровням вложенности, ус подходит к каждому узлу;
  - у **последнего** ребёнка в группе вертикаль обрывается на середине его строки, ниже пусто;
  - у промежуточных детей вертикаль идёт насквозь;
  - раскрыть/свернуть узел → линии перестраиваются корректно, «пеньков» не остаётся;
  - глубина 3+ уровня → на каждом уровне своя вертикаль, все выровнены по центрам шевронов;
  - лист и родитель на одном уровне выровнены (распорка = ширине шеврона);
  - `NodeTemplate` задан → линии не ломаются;
  - светлая и тёмная темы → линии видны, маска обрыва совпадает с фоном (нет полупрозрачного
    остатка);
- первичная загрузка (`clay-busy`) и спиннер узла (CTF5) не затронуты.
