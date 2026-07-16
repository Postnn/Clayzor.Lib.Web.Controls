> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Требует выполненного **GG7**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG9 — группировка по колонкам, которых нет в выводе: через диалог настройки колонок

Прочитать перед началом (обязательно, до написания кода):

- **`Components/Grid/ClayColumnSettingsDialog.razor` — целиком. Это тот диалог, который ты
  расширяешь. Особенно: параметр `ShowSorting`, поле `_dialogSortState`, `ToggleDialogSort`,
  `_syncSortToItems`, `GetDialogSortBadge`, `ResetSort`, `ResetAll`, `Apply`, снапшот
  `_originalItems`. Секция группировки делается ЗЕРКАЛЬНО секции сортировки.**
- `Components/Grid/ColumnSettingsItem.cs` — `SortPriority` (образец для `GroupPriority`), `IsReadonly`.
- `Components/Grid/ClayGrid.razor.cs` — `BuildColumnSettingsItems`, `OpenColumnSettings`,
  `_columnOrderSnapshot`.
- `Components/Grid/ClayGrid.ExportMenu.cs` — `ResolveExportColumnsAsync`: **второй потребитель
  этого диалога**. Смотри, как он гасит сортировку (`ShowSorting = false` + обнуление
  `SortPriority`) — грифовку надо погасить там же.
- `Components/Grid/ClayGrid.Grouping.cs` — `_groupColumns`, `AddGroupColumn`, `RemoveGroupColumn`,
  `IsGrouped`.
- `Components/Grid/ClayGrid.razor` — лоток группировки: цикл по `_groupColumns`, чипы,
  подсказка `grouping-tray-hint`.
- `Components/Grid/ClayGrid.DragDrop.cs` — `OnTrayDrop`, `OnChipDragStart`.

## Дефект

Данные и движок к группировке по невыводимой колонке готовы:

- после `GF4` колонки с `Порядок = 0` **зарегистрированы**: они есть в `_columnById`,
  `_columnBySqlName`, `_columnOrder` и в `_hiddenSqlNames`;
- после `GG7` у них `Groupable = true`;
- это НАСТОЯЩИЕ колонки источника — `GROUP BY` по ним корректен;
- `ApplySavedGroups` восстановит группировку по такой колонке из `grp+gridId`, и всё заработает:
  `GroupRowHostKey` пропускает `_hiddenSqlNames` и `IsGrouped`, `Hidden="@IsGrouped(sqlName)"`
  для уже скрытой колонки — no-op.

**Но в UI до них не добраться.** Чип попадает в лоток ровно двумя путями, и оба идут от
заголовка колонки:

- перетаскивание заголовка в лоток (`OnTrayDrop`);
- пункт «Группировать» в меню (⋮) заголовка.

У скрытой колонки заголовка нет — значит нет ни drag-источника, ни меню. Сгруппировать по ней
можно только вписав `grp140` в `vwНастройки` руками. Для динамического грида это типовой
сценарий: администратор ставит `Порядок = 0` колонке-классификатору (склад, отдел, статус),
которую в таблице показывать не нужно, а группировать по ней — нужно.

**Мой блок «Проверка» в GG7 говорит «перетащить заголовок в лоток» — для скрытых колонок это
невыполнимо. Это упущение, GG9 его закрывает.**

Место, где до колонки можно добраться независимо от её видимости, уже есть — это диалог
«Настройка колонок». После `GF6` он строится из `_columnOrder`, то есть **скрытая колонка в
нём уже присутствует** с выключенным переключателем. Не хватает одного: переключателя
группировки. Новый диалог не нужен.

## Изменить/создать

**Общий принцип шага: секция группировки — зеркало секции сортировки.** У сортировки уже есть
всё нужное: флаг показа, состояние в диалоге, бейдж приоритета, кнопка сброса, синхронизация в
`ColumnSettingsItem`. Повторяй её структуру и имена, а не изобретай свою.

**1.** `ColumnSettingsItem.cs`:

```csharp
    /// <summary>
    /// Приоритет группировки: 0 — не группируется, 1 — внешний уровень, 2 — следующий и т.д.
    /// Ограничения на число уровней нет (см. план GN).
    /// </summary>
    public int GroupPriority { get; set; }
```

**2.** `ClayColumnSettingsDialog.razor` — параметр:

```csharp
    /// <summary>
    /// Показывать переключатели группировки. По умолчанию false — диалог используется
    /// и для выбора колонок печати/экспорта, где группировка не настраивается.
    /// Грид включает его явно в OpenColumnSettings.
    /// </summary>
    [Parameter] public bool ShowGrouping { get; set; }
```

`ShowGrouping` по умолчанию **`false`**, в отличие от `ShowSorting` (там `true`). Так
`ResolveExportColumnsAsync` не придётся править — он просто не включит флаг.

**3.** `ClayColumnSettingsDialog.razor` — состояние и логика, зеркально сортировке:

```csharp
    /// <summary>
    /// Состояние группировки в диалоге: SqlName в порядке уровней.
    /// Первый элемент — внешний уровень.
    /// </summary>
    private readonly List<string> _dialogGroupState = [];

    /// <summary>
    /// Включает/выключает группировку по колонке. В отличие от сортировки — два состояния
    /// (нет → есть → нет), без ограничения на количество и БЕЗ направления.
    /// Новая колонка добавляется в КОНЕЦ: первая выбранная — внешний уровень.
    /// (Сортировка вставляет в начало — там свежий выбор логично делать главным,
    /// у группировки наоборот. Это осознанная разница, не копируй Insert(0, …).)
    /// </summary>
    private void ToggleDialogGroup(string sqlName)
    {
        var idx = _dialogGroupState.IndexOf(sqlName);
        if (idx >= 0)
            _dialogGroupState.RemoveAt(idx);
        else
            _dialogGroupState.Add(sqlName);

        _syncGroupToItems();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Синхронизирует GroupPriority и IsReadonly на всех элементах из _dialogGroupState.
    /// Сгруппированная колонка не выводится в таблице (Hidden="@IsGrouped(...)" в разметке
    /// грида), поэтому её переключатель видимости блокируется — как и было до GG9,
    /// но теперь пересчитывается живьём при кликах в диалоге.
    /// </summary>
    private void _syncGroupToItems()
    {
        foreach (var item in _items)
        {
            var idx = _dialogGroupState.IndexOf(item.SqlName);
            item.GroupPriority = idx >= 0 ? idx + 1 : 0;
            item.IsReadonly    = idx >= 0;
        }
    }

    /// <summary>Бейдж с номером уровня группировки. Пусто — колонка не группируется.</summary>
    private RenderFragment GetDialogGroupBadge(string sqlName) => builder =>
    {
        var idx = _dialogGroupState.IndexOf(sqlName);
        if (idx < 0) return;
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "chip-group-badge");
        builder.AddContent(2, (idx + 1).ToString());
        builder.CloseElement();
    };

    /// <summary>Сбрасывает группировку (грид станет плоским).</summary>
    private void ResetGroup()
    {
        _dialogGroupState.Clear();
        _syncGroupToItems();
        InvokeAsync(StateHasChanged);
    }
```

Бейдж без стрелки — у группировки нет направления. Порядок уровней задаёт `_dialogGroupState`,
а не порядок колонок в списке: пользователь мог выстроить чипы в лотке перетаскиванием, и
открытие диалога не должно этот порядок ломать. **Не выводи порядок группировки из позиции в
`_items`.**

**4.** `ClayColumnSettingsDialog.razor` — `OnInitialized`: восстановить состояние из входных
`Items` (зеркало сортировки) и включить снапшот:

```csharp
        if (ShowGrouping)
        {
            foreach (var item in _items.Where(i => i.GroupPriority > 0).OrderBy(i => i.GroupPriority))
                _dialogGroupState.Add(item.SqlName);
        }
```

`_originalItems` (снапшот для «Восстановить по-умолчанию») копирует поля поимённо — **добавь
туда `GroupPriority = i.GroupPriority`**, иначе кнопка сброса потеряет группировку. В `ResetAll`
поле уже перечислено рядом с `SortPriority = 0`: там для группировки тоже `GroupPriority = 0`,
плюс `_dialogGroupState.Clear()` + `_syncGroupToItems()` под `if (ShowGrouping)` — зеркально
блоку сортировки. **Прочитай оба метода и повтори их структуру, не пиши по памяти.**

**5.** `ClayColumnSettingsDialog.razor` — разметка. В шапке списка, рядом с иконками
«Видимость» и «Фильтр по значению», третья колонка:

```razor
                    @if (ShowGrouping)
                    {
                        <MudTooltip Text="Группировка" Placement="Placement.Top">
                            <div style="width:38px;display:flex;justify-content:center">
                                <MudIcon Icon="@Icons.Material.Filled.AccountTree"
                                         Size="Size.Small"
                                         Style="opacity:0.55" />
                            </div>
                        </MudTooltip>
                    }
```

В строке колонки, в правом блоке (`@onpointerdown:stopPropagation`), рядом с переключателями:

```razor
                        @if (ShowGrouping)
                        {
                            <div style="width:38px;display:flex;justify-content:center;align-items:center">
                                @if (item.Groupable)
                                {
                                    <MudTooltip Text="@(item.GroupPriority > 0 ? "Убрать из группировки" : "Группировать по этой колонке")"
                                                Placement="Placement.Top">
                                        <div class="clay-group-toggle" @onclick="() => ToggleDialogGroup(item.SqlName)">
                                            <MudIcon Icon="@Icons.Material.Filled.AccountTree"
                                                     Size="Size.Small"
                                                     Color="@(item.GroupPriority > 0 ? Color.Primary : Color.Default)" />
                                            @GetDialogGroupBadge(item.SqlName)
                                        </div>
                                    </MudTooltip>
                                }
                            </div>
                        }
```

Пустой `div` фиксированной ширины для негруппируемых колонок обязателен — иначе строки списка
разъедутся по вертикали.

Понадобится ещё одно поле в `ColumnSettingsItem`:

```csharp
    /// <summary>Разрешена ли группировка по колонке (ClayColumnMeta.Groupable).</summary>
    public bool Groupable { get; init; }
```

Класс `clay-group-toggle` и `chip-group-badge` — в `wwwroot/css/app.css`. `chip-group-badge`
делай по образцу соседнего `chip-sort-badge` (посмотри его и повтори), `clay-group-toggle` —
просто `cursor:pointer` + выравнивание.

**6.** `ClayColumnSettingsDialog.razor` — кнопка сброса в `DialogActions`, рядом с «Сбросить
сортировку»:

```razor
            @if (ShowGrouping)
            {
                <MudTooltip Text="Сбросить группировку" Placement="Placement.Top">
                    <MudIconButton Icon="@Icons.Material.Filled.LayersClear"
                                   Size="Size.Small"
                                   OnClick="ResetGroup" />
                </MudTooltip>
            }
```

**7.** `ClayGrid.razor.cs`, `BuildColumnSettingsItems` — заполнить новые поля. Блок
`GroupPriority` ставь рядом с уже существующим блоком `SortPriority`:

```csharp
        var items = _columnOrder
            .Select(id => _columnById.GetValueOrDefault(id))
            .Where(m => m is not null)
            .Select(m => new ColumnSettingsItem
            {
                SqlName          = m!.SqlName,
                DisplayName      = m.DisplayName,
                IsVisible        = !_hiddenSqlNames.Contains(m.SqlName) && !IsGrouped(m.SqlName),
                IsReadonly       = IsGrouped(m.SqlName),
                Groupable        = m.Groupable,
                AllowValueFilter = !_valueFilterDisabledColumns.Contains(m.SqlName) && m.AllowValueFilter,
            })
            .ToList();

        for (int i = 0; i < _groupColumns.Count; i++)
        {
            var match = items.FirstOrDefault(it => it.SqlName == _groupColumns[i]);
            if (match is not null)
                match.GroupPriority = i + 1;
        }

        /* … существующий блок SortPriority без изменений … */
```

Сборка списка из `_columnOrder` — это GF6; если у тебя там всё ещё `_columnBySqlName.Values.OrderBy(...)`,
значит GF6 не выполнен — **ОСТАНОВИСЬ**, без него в диалог попадут фильтр-онли колонки.

**8.** `ClayGrid.razor.cs`, `OpenColumnSettings` — включить флаг и применить результат:

```csharp
        var parameters = new DialogParameters<ClayColumnSettingsDialog>
        {
            { x => x.Items,        items },
            { x => x.ShowGrouping, _columnById.Values.Any(m => m.Groupable) },
        };
```

и в блоке применения, ПОСЛЕ блока `_sortState`:

```csharp
            _groupColumns.Clear();
            foreach (var item in updatedItems.Where(i => i.GroupPriority > 0).OrderBy(i => i.GroupPriority))
                _groupColumns.Add(item.SqlName);

            if (Dynamic) ResetDynamicExpandedGroups();   // GG7: ключи старой группировки протухли
```

`ResetDynamicExpandedGroups` объявлен в `ClayGrid.Dynamic.Grouping.cs` (GG7). Если его нет —
GG7 не выполнен, **ОСТАНОВИСЬ**.

**9. Попутный дефект — почини его здесь, он становится центральным.** Тот же блок применения
начинается так:

```csharp
            _hiddenSqlNames.Clear();
            _columnOrder.Clear();
            foreach (var item in updatedItems)
            {
                if (!item.IsVisible)
                    _hiddenSqlNames.Add(item.SqlName);
                ...
```

а `BuildColumnSettingsItems` даёт `IsVisible = !hidden && !IsGrouped(...)`. То есть у
сгруппированной колонки `IsVisible == false` **не потому, что пользователь её выключил, а
потому что её прячет группировка** — и на «Применить» она навсегда уезжает в `_hiddenSqlNames`.
Снял группировку — колонка не вернулась, её надо включать руками. Дефект есть и сегодня (через
меню ⋮ заголовка), но после GG9 группировка настраивается прямо в этом диалоге, и он начнёт
стрелять постоянно.

Правка:

```csharp
            // Невидимость сгруппированной колонки — следствие группировки, а не выбора
            // пользователя. Записывать её в _hiddenSqlNames нельзя: снимут группировку —
            // колонка обязана вернуться. Для таких колонок сохраняем прежний признак.
            var hiddenBefore = new HashSet<string>(_hiddenSqlNames);

            _hiddenSqlNames.Clear();
            _columnOrder.Clear();
            foreach (var item in updatedItems)
            {
                var hidden = item.GroupPriority > 0
                    ? hiddenBefore.Contains(item.SqlName)
                    : !item.IsVisible;
                if (hidden)
                    _hiddenSqlNames.Add(item.SqlName);
                if (_columnBySqlName.TryGetValue(item.SqlName, out var meta2))
                    _columnOrder.Add(meta2.ColumnId);
            }
```

**10.** `ClayGrid.razor` — кнопка в лотке. Открывает ТОТ ЖЕ диалог, что и «Настройка колонок»
в тулбаре. Ставится после цикла по `_groupColumns` и до подсказки:

```razor
            <MudIconButton Icon="@Icons.Material.Filled.Add"
                           Size="Size.Small"
                           OnClick="OpenColumnSettings"
                           Class="grouping-tray-add-btn pa-0"
                           title="Выбрать колонки для группировки" />

            @if (_groupColumns.Count == 0)
            {
                <span class="grouping-tray-hint">
                    Перетащите заголовок колонки сюда или нажмите «+»
                </span>
            }
```

Текст подсказки поправь: сейчас он говорит только про перетаскивание, а это не единственный
и не всегда возможный путь.

Класс `grouping-tray-add-btn` — в `app.css`, рядом с `grouping-tray-hint` и
`clay-grid-chip-btn--gold`; посмотри, как оформлены соседи, и сделай так же.

**11.** `wwwroot/css/app.css`, `@media print` — кнопка «+» не должна попадать в печатную форму.
Найди правило, скрывающее `.chip-remove-btn`, и добавь туда `.grouping-tray-add-btn`. Тот же
список продублирован в `Services/ClayGridPrintHtmlGenerator.cs` → `EmbedStyles` — **проверь и
там**. Строго говоря, лоток в печать не идёт целиком (`.grouping-tray{display:none}`), но
список скрываемых элементов должен оставаться согласованным.

## Не делай

**Не создавай новый диалог** — `ClayColumnSettingsDialog` уже умеет список колонок, drag-порядок,
переключатели и бейджи приоритета; вторая такая же сущность — это два места для одного и того
же и гарантированное расхождение. Не трогай `ResolveExportColumnsAsync`: `ShowGrouping` по
умолчанию `false`, экспорт флаг не включает — **но проверь, что он не сломался**, он
переиспользует `BuildColumnSettingsItems`. Не трогай `OnTrayDrop`, `OnChipDragStart`, меню (⋮)
заголовка — существующие пути остаются. Не меняй `AddGroupColumn`/`RemoveGroupColumn` — они
нужны drag-пути. Не копируй у сортировки ограничение «не более 2 колонок» — у группировки
уровней сколько угодно (см. план GN). Не копируй `Insert(0, …)` — группировка добавляется в
конец. Не делай скрытые колонки видимыми при группировке по ним.

Фильтр-онли колонки (Тип 6/11) в диалог не попадают: их нет в `_columnOrder` (GF6), и
`Groupable` у них `false`. Это правильно и остаётся: у них нет колонки-источника, `GROUP BY`
строить не по чему (см. `ClayConditionBoolColumnType`, `ClayConditionListColumnType`: `Формат` —
это предикат, а `Колонка` — идентификатор).

## Проверка (ручная)

У грида 140 колонка `Активно` имеет `Порядок = 0` (скрыта по умолчанию).

`?id=140&CLID=9`, диалог «Настройка колонок» из тулбара:

- появилась третья колонка переключателей с иконкой дерева; у негруппируемых колонок на её
  месте пусто, строки не разъехались;
- `Активно` в списке ЕСТЬ (GF6), её переключатель видимости выключен;
- **кликнуть иконку группировки у `Активно` → бейдж «1», переключатель видимости заблокировался;
  «Применить» → грид сгруппировался по скрытой колонке**: заголовки групп «Да»/«Нет» со
  счётчиками, чип «Активно» в лотке, самой колонки в таблице нет. Это приёмка шага;
- раскрыть группу → строки на месте, счётчик верный;
- открыть диалог снова → у `Активно` бейдж «1»;
- кликнуть иконку ещё раз → бейдж исчез, переключатель видимости разблокировался;
  «Применить» → грид плоский, чип исчез, **`Активно` осталась скрытой** (она и была скрытой
  по умолчанию — п. 9);
- **проверка п. 9 на видимой колонке**: сгруппировать по `Тип исследования` (она видима),
  «Применить» → колонка ушла из таблицы; снять группировку, «Применить» → **колонка ВЕРНУЛАСЬ
  в таблицу сама**. До правки она бы осталась скрытой;
- отметить группировку у двух колонок → бейджи «1» и «2» в порядке кликов; «Применить» →
  двухуровневая группировка в том же порядке;
- переставить чипы в лотке перетаскиванием, открыть диалог → бейджи отражают НОВЫЙ порядок
  чипов, а не порядок колонок в списке;
- «Сбросить группировку» (иконка в углу диалога) → все бейджи исчезли; «Применить» → грид плоский;
- «Восстановить по-умолчанию» → порядок, видимость, сортировка и группировка вернулись к
  состоянию на момент открытия диалога;
- «Отмена» → ничего не изменилось, запроса к БД не было;
- «Настройка колонок» → включить `Активно` в вывод, «Применить»; открыть снова → её
  переключатель видимости включён, группировка независима.

Лоток:

- «Группировать» в тулбаре → в лотке кнопка «+» и подсказка
  «Перетащите заголовок колонки сюда или нажмите «+»»;
- «+» → открылся тот же диалог «Настройка колонок» с включённой секцией группировки;
- перетащить заголовок «Тип исследования» в лоток (старый путь) → работает как раньше;
- сгруппировать по `Активно`, F5 → группировка восстановилась из `grp140` (GG7);
- **печать**: сгруппировать по скрытой колонке, «Печать → Текущая страница» → в форме строки
  групп есть, кнопки «+» нет.

Экспорт — регрессия (второй потребитель диалога):

- «Excel → Текущая страница» → «Настроить» → диалог БЕЗ секции группировки и БЕЗ секции
  сортировки, только видимость и порядок; выбор применяется к файлу; грид не изменился.

Статический режим (`MedicalTests.razor`): секция группировки появилась и там; drag, меню (⋮),
чипы, уровни, сортировка в диалоге — работают как раньше; п. 9 проверить так же (сгруппировать
видимую колонку → снять группировку → колонка вернулась).
