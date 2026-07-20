> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md**. Требует выполненного **GB8** (сохранение идёт в БД из обработчика — доступ к соединению должен быть сериализован). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB11 — перетаскивание колонок в шапке грида не сохраняется в настройках

Прочитать перед началом: `Components/Grid/ClayGrid.razor.cs` — `OnColumnDrop` целиком,
`OpenColumnSettings` (как там после изменения `_columnOrder` вызывается `NotifyQueryChanged`),
`NotifyQueryChanged`; `Components/Grid/ClayGrid.Dynamic.cs` — `LoadDynamicData` (хвост с
`SaveDynamicState`), `SaveDynamicState`, `SaveParamIfChanged`, `_dynamicSavedParams`;
`Components/Grid/Dynamic/GridStateSerializer.cs` — `SerializeColumns`;
`wwwroot/js/clayGridColumnDrag.js` — вызов `dotnetRef.invokeMethodAsync('OnColumnDrop', …)`;
`GF12_save_only_changed.md` — как устроено «писать только изменившееся».

## Дефект

Перетаскивание заголовка колонки в самом гриде меняет порядок только в памяти и не сохраняет
его. После F5 (или повторного открытия грида) порядок возвращается к сохранённому в `vwНастройки`.

`OnColumnDrop` (вызывается из JS `clayGridColumnDrag.js` после drop) переставляет `_columnOrder`
и на этом заканчивается:

```csharp
[JSInvokable]
public void OnColumnDrop(string srcSql, string targetSql, bool insertBefore)
{
    …
    _columnOrder.Insert(insertAt, srcId);

    _dataKey++;
    InvokeAsync(StateHasChanged);   // ← только перерисовка, ни NotifyQueryChanged, ни SaveDynamicState
}
```

Для сравнения — второй путь смены порядка, диалог «Настройка колонок» (`OpenColumnSettings`),
после правки `_columnOrder` вызывает `NotifyQueryChanged()`, а тот в динамическом режиме уходит
в `LoadDynamicData`, в хвосте которого стоит `await SaveDynamicState()`. Поэтому порядок,
заданный через диалог, сохраняется, а тот же порядок, заданный перетаскиванием, — нет. Ровно
то, на что жалуется тестировщик.

Данные (`Items`) при перестановке колонок не меняются — двигаются только столбцы. Поэтому
`OnColumnDrop` намеренно НЕ звал `NotifyQueryChanged` (перезагрузка строк из БД здесь не нужна).
Правильно не «добавить перезагрузку данных», а «сохранить состояние без перезагрузки данных».

`SaveDynamicState` для этого уже пригоден: он идемпотентен и пишет только изменившиеся параметры
(GF12, кеш `_dynamicSavedParams`) — вызвать его повторно дёшево, лишних `INSERT` не будет,
изменится только параметр колонок.

Статический режim порядок колонок в БД вообще не хранит (он живёт только в текущем сеансе
страницы и сбрасывается при перезагрузке независимо ни от чего) — там сохранять нечего, и
трогать статику не нужно.

## Изменить/создать

`Components/Grid/ClayGrid.razor.cs`, `OnColumnDrop` — сделать `async`, вынести сохранение
состояния в динамическом режиме. Метод остаётся `[JSInvokable]`; JS-вызов
`invokeMethodAsync('OnColumnDrop', …)` работает и с `Task`-возвращающим методом.

```csharp
/// <summary>
/// Вызывается из JS после drag-and-drop заголовка колонки.
/// Обновляет <see cref="_columnOrder"/> через insert (не swap) и в динамическом режиме
/// сохраняет новый порядок (данные не перезагружаются — двигаются только столбцы).
/// </summary>
[JSInvokable]
public async Task OnColumnDrop(string srcSql, string targetSql, bool insertBefore)
{
    if (!_columnBySqlName.TryGetValue(srcSql, out var srcMeta)) return;
    if (!_columnBySqlName.TryGetValue(targetSql, out var tgtMeta)) return;

    var srcId = srcMeta.ColumnId;
    var tgtId = tgtMeta.ColumnId;

    var srcIdx = _columnOrder.IndexOf(srcId);
    var tgtIdx = _columnOrder.IndexOf(tgtId);
    if (srcIdx < 0 || tgtIdx < 0 || srcIdx == tgtIdx) return;

    _columnOrder.RemoveAt(srcIdx);
    tgtIdx = _columnOrder.IndexOf(tgtId);
    var insertAt = insertBefore ? tgtIdx : tgtIdx + 1;
    insertAt = Math.Clamp(insertAt, 0, _columnOrder.Count);
    _columnOrder.Insert(insertAt, srcId);

    _dataKey++;
    StateHasChanged();

    // Порядок колонок — часть сохраняемого состояния. Диалог настройки колонок
    // сохраняет его через NotifyQueryChanged → LoadDynamicData → SaveDynamicState;
    // перетаскивание данные не меняет, поэтому сохраняем состояние напрямую, без
    // перезагрузки строк. SaveDynamicState идемпотентен и пишет только изменившийся
    // параметр (GF12).
    if (Dynamic)
        await SaveDynamicState();
}
```

`InvokeAsync(StateHasChanged)` → `StateHasChanged()`: метод теперь исполняется в контексте
компонента (Blazor вызывает `[JSInvokable]` в его синхронизационном контексте), обёртка
`InvokeAsync` не нужна. Если сборка/поведение покажут обратное — верни `InvokeAsync`, но не молча.

Больше ничего не трогать: ни `SaveDynamicState`, ни `GridStateSerializer.SerializeColumns`
(порядок и скрытые колонки он уже сериализует правильно — тем же вызовом пользуется диалог),
ни JS.

## Не делай

- Не вызывай `NotifyQueryChanged` из `OnColumnDrop` — это лишний SELECT данных на каждое
  перетаскивание колонки. Данные не изменились, нужно сохранить только состояние.
- Не трогай статический режим и `ClayGridPageBase` — там порядок колонок в БД не хранится,
  сохранять нечего. Если заказчику нужна персистентность порядка и для статических гридов —
  это отдельная фича (своё хранилище состояния), не багфикс.
- Не дублируй логику сериализации в `OnColumnDrop` — только вызов `SaveDynamicState()`.
- Не убирай `_dataKey++` / `StateHasChanged` — без них грид не перерисует новый порядок колонок.
- Не трогай `clayGridColumnDrag.js` и контракт `OnColumnDrop(srcSql, targetSql, insertBefore)`.
- Не сохраняй состояние в обход `SaveParamIfChanged` — потеряешь защиту от лишних записей (GF12).

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`: перетащить колонку в шапке грида на новое место →
  порядок изменился визуально; **F5** → порядок сохранился;
- перетащить ещё раз, вернуть примерно назад → F5 → сохранился новый порядок;
- порядок, заданный перетаскиванием, затем открыть «Настройка колонок» → в диалоге порядок
  соответствует тому, что в гриде (диалог и грид согласованы);
- задать порядok через диалог, затем поправить перетаскиванием, F5 → виден последний
  (перетащенный) порядок;
- в SQL-профайлере: одно перетаскивание → максимум один `INSERT/UPDATE` в `vwНастройки`
  (только параметр колонок); повторное перетаскивание в то же положение (порядок фактически
  не изменился) → записи в БД нет (`SaveParamIfChanged` отсёк по кешу);
- перетаскивание колонки НЕ вызывает перезагрузку строк (в профайлере нет SELECT данных грида);
- скрыть колонку через диалог, затем перетащить другую → F5 → и скрытие, и новый порядок на месте
  (`SerializeColumns` пишет и порядок, и `_hiddenSqlNames`);
- forced-колонки из URL (если для грида заданы) перетаскиванием не «залипают» в сохранённом
  состоянии — `SaveParamIfChanged` пропускает forced-параметры (регрессия GF12);
- быстро перетащить несколько колонок подряд → исключения MARS нет (GB8 сериализует доступ
  к соединению; проверь, что сохранение из обработчика не конфликтует с фоновой загрузкой);
- статический режим (`/medical-tests`): перетаскивание колонок работает в пределах сеанса
  как раньше, после F5 порядок сбрасывается к исходному (persistence не заявлена) — поведение
  не изменилось;
- `dotnet build` + `dotnet test` — зелёные.
