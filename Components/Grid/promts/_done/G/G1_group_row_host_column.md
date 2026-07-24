# G1. Группировка: единая «хост-колонка» для строки-заголовка вместо привязки к произвольной колонке потребителя

Часть 1 из 2 (см. также `G2_group_row_fullwidth_css.md`). G1 — чисто функциональная правка
(какая колонка ЧТО показывает), G2 — визуальное объединение ячеек в полосу на всю ширину.

Файлы:
- `Components/Grid/ClayGrid.razor` — сервисные колонки чекбокса/редактирования + цикл колонок данных.
- `Components/Grid/ClayGrid.razor.cs` или `ClayGrid.Grouping.cs` — новый параметр `OnGroupToggle`,
  вычисление хост-колонки.
- `Components/Grid/ClayGroupHeader.razor` — без изменений в разметке, меняется только место вызова.
- Страницы-потребители вне `Clayzor.Lib.Web.Controls` (в основном веб-проекте решения), которые сегодня
  вручную кладут `<ClayGroupHeader>` внутрь `CellTemplate` одной из колонок — найти по
  `grep -rn "ClayGroupHeader" --include=*.razor` и `grep -rn "is GroupHeaderRow"`.

## Проблема (скриншоты 1 и 2)

Грид сегодня ничего не знает о `GroupHeaderRow`. Рендер строки-заголовка группы целиком на совести
страницы-потребителя: она сама решает, в `CellTemplate` какой колонки написать

```razor
@if (context.Item is GroupHeaderRow header)
{
    <ClayGroupHeader Header="header" OnToggle="ToggleGroup" />
}
```

По соглашению это обычно «первая» колонка (см. doc-comment в `ClayGroupHeader.razor`). Отсюда два
взаимосвязанных дефекта:

1. Сервисные колонки — чекбокс выбора (`ClayGrid.razor`, блок `_selectMode`) и колонка редактирования
   (блок `EditDialogType is not null`) — рендерятся ДО пользовательских колонок и ничего не знают про
   группу, поэтому остаются пустыми ячейками слева от заголовка. Визуально плашка группы «прибита» не к
   левому краю таблицы, а к какой-то внутренней колонке (скриншот 1: пустая ячейка редактирования слева
   от «COVID-19»).
2. Колонка, которая скрывается при группировке по ней (`Hidden="@IsGrouped(sqlName)"` — см.
   `ClayColumn.razor._hidden` и цикл в `ClayGrid.razor`), очень часто оказывается той же колонкой, в
   `CellTemplate` которой лежит `<ClayGroupHeader>`. Стоит сгруппировать по ней — и единственное место,
   где вообще печатается заголовок группы, исчезает вместе с колонкой. Результат — пустые строки без
   заголовков (скриншот 2, группировка по «Тип» и «Код»).

## Идея решения

Грид сам определяет **одну** ячейку в строке, обязанную показывать заголовок группы — и это решение
никогда не зависит от того, какая колонка сейчас скрыта группировкой. Все остальные ячейки строки для
`GroupHeaderRow` рендерят пустое содержимое. Приоритет:

1. Колонка редактирования (`EditDialogType is not null`) — она никогда не скрывается группировкой,
   поэтому если она есть, хост — всегда она (самый стабильный вариант, не «прыгает» при перегруппировке).
2. Иначе — первая по порядку отображения видимая колонка данных, которая сейчас не скрыта пользователем
   и не участвует в группировке.
3. Чекбокс выбора — не хост, у него отдельная роль: при `_selectMode == true` для `GroupHeaderRow` он,
   как и сегодня, показывает трёхстороннее состояние выбора группы (`ComputeGroupCheckState`,
   `OnGroupTriToggle` в `ClayGrid.Grouping.cs`) — это уже работает, не трогаем.

## Правки

### 1. Вычисление хост-ключа и новый параметр

Разместить рядом с остальным кодом группировки (`ClayGrid.Grouping.cs`) — один источник истины:

```csharp
/// <summary>
/// Событие переключения раскрытия/сворачивания группы.
/// Страница-потребитель подписывается через `OnGroupToggle="ToggleGroup"` на теге &lt;ClayGrid&gt;.
/// Больше не нужно вручную встраивать &lt;ClayGroupHeader&gt; в CellTemplate конкретной колонки.
/// </summary>
[Parameter] public EventCallback<GroupHeaderRow> OnGroupToggle { get; set; }

/// <summary>
/// SqlName колонки, которая должна отображать заголовок группы (шеврон + подпись + счётчик).
/// "__edit__" — колонка редактирования. Никогда не совпадает с колонкой, скрытой текущей
/// группировкой или пользовательскими настройками — вычисляется заново на каждый рендер.
/// </summary>
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

private bool IsGroupRowHost(string sqlName) => GroupRowHostKey == sqlName;
```

### 2. Колонка редактирования — теперь может быть хостом

`ClayGrid.razor`, блок `EditDialogType is not null`:

```razor
<CellTemplate>
    @if (context.Item is GroupHeaderRow gh)
    {
        @if (GroupRowHostKey == "__edit__")
        {
            <ClayGroupHeader Header="gh" OnToggle="OnGroupToggle" />
        }
    }
    else if (context.Item is IDetailRow detail)
    {
        <ClayButton Icon="@Icons.Material.Filled.Edit"
                     Text="Редактировать"
                     Size="Size.Small"
                     OnClick="async () => await HandleEditClick(detail)" />
    }
</CellTemplate>
```

### 3. Цикл колонок данных — тоже может быть хостом, иначе ничего не рендерит для `GroupHeaderRow`

`ClayGrid.razor`, `@foreach (var colId in _columnOrder)`. Перед вызовом `cellTemplate(context)`
добавить проверку типа строки:

```razor
<CellTemplate>
    @if (context.Item is GroupHeaderRow gh)
    {
        @if (IsGroupRowHost(sqlName))
        {
            <ClayGroupHeader Header="gh" OnToggle="OnGroupToggle" />
        }
    }
    else
    {
        @{ var cellTemplate = (RenderFragment<CellContext<TEntity>>)templateObj; }
        @cellTemplate(context)
    }
</CellTemplate>
```

Важно: теперь `CellTemplate` каждой колонки данных, который задаёт страница-потребитель, **больше не
вызывается** гридом для строк-заголовков групп. Проверять `is GroupHeaderRow` внутри него больше не
обязательно — грид гарантированно туда не зайдёт.

### 4. Найти и очистить страницы-потребители

На страницах вне `Clayzor.Lib.Web.Controls`, которые сегодня вручную вставляют

```razor
@if (context.Item is GroupHeaderRow header) { <ClayGroupHeader Header="header" OnToggle="ToggleGroup" /> }
```

внутрь `CellTemplate` какой-то одной колонки:

1. Убрать оттуда явную вставку `<ClayGroupHeader>` — это теперь делает грид сам.
2. На теге `<ClayGrid ...>` этой страницы добавить `OnGroupToggle="ToggleGroup"`.
3. Если в `CellTemplate` колонки была только эта проверка — просто убрать ветку
   `if (... is GroupHeaderRow)`, оставив обработку `DetailRow<T>` как есть.

## Критерии

- [ ] `GroupRowHostKey` / `IsGroupRowHost` — единственное место, решающее, какая колонка хостит
      заголовок; не зависит от того, какая колонка сейчас скрыта.
- [ ] Группировка по колонке, которая раньше «хостила» заголовок, больше не приводит к пустым строкам
      (сценарий скриншота 2 — группировка по «Тип» и «Код»).
- [ ] На страницах-потребителях не осталось ручной вставки `<ClayGroupHeader>` внутри `CellTemplate` —
      управление полностью в `ClayGrid`, подписка только через `OnGroupToggle`.
- [ ] Трёхстороннее выделение группы в чекбокс-колонке работает как раньше (регрессии нет).
- [ ] `dotnet build` без ошибок по всему решению (включая страницы-потребители, если они в том же
      solution).
