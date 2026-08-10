# CTM5 — Tooltip обрезанного названия и выделение текущей ноды

## Цель

Два UX-требования:
1. Если название узла не помещается по ширине контейнера — при наведении показывать tooltip с
   полным названием (только когда реально обрезано).
2. Текущая (выделенная) нода визуально отличается от остальных в стиле приложения. Выделение не
   влияет на разворачивание/сворачивание. Событие выделения уже реализовано (`OnNodeClick`,
   `HandleClick`) — его не трогаем.

Часть выделения по факту уже частично есть: в разметке узла применяется класс
`clay-tree-node--selected` (см. `ClayTreeNodeView.razor`). Задача — убедиться, что стиль выражен
и соответствует палитре приложения, и добавить tooltip.

## Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeNodeView.razor`

Найти вывод текста узла (ветка без `NodeTemplate`):

```razor
<span class="clay-tree-node-text" @onclick="HandleClick" @onclick:stopPropagation="true">
    @Node.Text
</span>
```

Заменить на вариант с определением обрезки и нативным tooltip через атрибут `title`, проставляемый
из JS (нативный `title` надёжнее и не тянет зависимости):

```razor
<span class="clay-tree-node-text"
      @ref="_textRef"
      @onclick="HandleClick" @onclick:stopPropagation="true"
      title="@(_isTruncated ? Node.Text : null)">
    @Node.Text
</span>
```

## Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeNodeView.razor.cs`

**2.1.** Добавить поля:

```csharp
    private ElementReference _textRef;
    private bool _isTruncated;
```

**2.2.** Добавить проверку обрезки после рендера. Если в файле уже есть
`OnAfterRenderAsync` — дополнить его; иначе добавить:

```csharp
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // существующая логика (если была) остаётся выше/ниже этого блока

        // Определяем, обрезан ли текст: scrollWidth > clientWidth
        try
        {
            var truncated = await JS.InvokeAsync<bool>("clayTree.isTextTruncated", _textRef);
            if (truncated != _isTruncated)
            {
                _isTruncated = truncated;
                StateHasChanged();
            }
        }
        catch (JSDisconnectedException) { /* circuit закрыт — игнорируем */ }
        catch (ObjectDisposedException) { /* JS-рантайм освобождён */ }
    }
```

Убедиться, что есть `[Inject] private IJSRuntime JS { get; set; } = default!;` и
`using Microsoft.JSInterop;`.

## Шаг 3 — JS-функция определения обрезки

Найти существующий JS-файл дерева в `Clayzor.Lib.Web.Controls/wwwroot/js/` (например
`clayTreePaging.js`). Если есть общий объект `clayTree` — добавить в него; если нет — создать файл
`clayTree.js` и подключить его в разметку/`_Host` там же, где подключены остальные скрипты дерева.

Функция:

```javascript
window.clayTree = window.clayTree || {};
window.clayTree.isTextTruncated = function (el) {
    if (!el) return false;
    return el.scrollWidth > el.clientWidth;
};
```

Если создаётся новый файл — подключить его тем же способом, что и прочие скрипты компонента
(проверить, как подключён `clayTreePaging.js`, и сделать по аналогии).

## Шаг 4 — файл стилей дерева (`clay-tree.css`)

**4.1.** Текст узла должен уметь обрезаться (иначе `scrollWidth` не превысит `clientWidth`):

```css
.clay-tree-node-text {
    display: inline-block;
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    vertical-align: middle;
}
```

**4.2.** Выделение текущей ноды — выразительный стиль в палитре приложения. Проверить,
существует ли уже правило `.clay-tree-node--selected`; привести к виду:

```css
.clay-tree-node--selected > .clay-tree-node-row {
    background: var(--lh-navy, #05164D);
    border-radius: 4px;
}
.clay-tree-node--selected > .clay-tree-node-row .clay-tree-node-text {
    color: var(--lh-white, #fff);
    font-weight: 600;
}
```

Если переменные палитры в проекте называются иначе — использовать фактические (поискать `--lh-`
или `--mud-palette-primary` в стилях проекта и взять принятые). Выделение применяется к строке
узла (`.clay-tree-node-row`), НЕ ко всему поддереву, чтобы не подсвечивать детей.

## Критерии приёмки

- Длинное название с узким контейнером обрезается многоточием; при наведении — нативный tooltip с
  полным текстом. Короткое название tooltip не показывает.
- Клик по узлу выделяет его контрастным стилем приложения; ранее выделенный снимается (режим
  Single). Разворачивание/сворачивание при этом не меняется.
- Выделение подсвечивает только строку узла, не его детей.
- Уход со страницы во время рендера не роняет circuit (JS-исключения проглочены аккуратно).
