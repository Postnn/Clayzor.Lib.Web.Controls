> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF11 — `_clientOffset`: получить часовой пояс клиента (Тип 10/13)

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — `_clientOffset`, сборка
cell-шаблона для `ClayColumnKind.DateTimeLocal` / `TimeLocal`; `Components/Grid/Dynamic/ClayDateTimeConverter.cs`
— `Format`, `ConvertFromUtc`; `Components/Grid/ClayGrid.razor.cs` — `OnAfterRenderAsync`, `_dataKey`,
`_columnsReady`; `wwwroot/js/clayGridColumnDrag.js` — как оформлен существующий JS-модуль;
`Kesco.App.Web.Inventory/Components/App.razor` — режим рендеринга и список подключённых скриптов;
`promts/G14_type_datetime_local.md` — исходное требование.

## Дефект

```csharp
// Смещение часового пояса клиента (для Тип 10/13)
private TimeSpan _clientOffset = TimeSpan.Zero;
```

Присвоения нет нигде — поле навсегда `TimeSpan.Zero`. Cell-шаблон вызывает
`ClayDateTimeConverter.Format(v, dtFormat, _clientOffset)`, `ConvertFromUtc` делает
`utc.Value + offset` с нулевым смещением → локальное время равно UTC. Тип 10 (`DateTimeLocal`)
и Тип 13 (`TimeLocal`) сейчас неотличимы от Тип 3 (`Date`), то есть G14 фактически не работает.

Сложность в пререндере: `<Routes @rendermode="InteractiveServer" />` означает серверный проход,
где JS недоступен. `OnInitializedAsync` (и `InitDynamicMode` внутри) на нём выполняется, а
`IJSRuntime` вызывать нельзя — упадёт `InvalidOperationException`. Значит смещение берётся
только в `OnAfterRenderAsync(firstRender)` и требует перерисовки уже отрендеренных ячеек.

## Изменить/создать

**1.** JS-модуль `wwwroot/js/clayGridTimeZone.js` (по образцу `clayGridColumnDrag.js`):

```javascript
// ── Часовой пояс клиента для ClayGrid (Тип 10/13) ─────────────────────────────
window.clayGridTimeZone = {
    /**
     * Смещение локального пояса от UTC в минутах.
     * getTimezoneOffset() возвращает ОБРАТНЫЙ знак (UTC+3 → -180), поэтому инвертируем.
     * @returns {number} минуты, напр. 180 для UTC+3
     */
    getOffsetMinutes: function () {
        return -new Date().getTimezoneOffset();
    }
};
```

Знак — главная ловушка: `Date.prototype.getTimezoneOffset` для Москвы (UTC+3) возвращает `-180`.
Инвертируем в JS, чтобы C# получал уже привычное «+180 = UTC+3».

**2.** Подключить скрипт в `Kesco.App.Web.Inventory/Components/App.razor` рядом с остальными
`_content/Clayzor.Lib.Web.Controls/js/...`.

**3.** `ClayGrid.Dynamic.cs` — метод получения смещения:

```csharp
/// <summary>
/// Читает смещение часового пояса клиента через JS. Вызывается только из
/// OnAfterRenderAsync(firstRender): при пререндере JS недоступен.
/// </summary>
private async Task InitClientOffset()
{
    try
    {
        var minutes = await JS.InvokeAsync<int>("clayGridTimeZone.getOffsetMinutes");
        var offset  = TimeSpan.FromMinutes(minutes);
        if (offset == _clientOffset) return;

        _clientOffset = offset;
        _dataKey++;              // пересобрать ячейки с уже новым смещением
        StateHasChanged();
    }
    catch
    {
        // JS недоступен (пререндер/отвал) — остаёмся на UTC
    }
}
```

**4.** `ClayGrid.razor.cs`, `OnAfterRenderAsync` — вызвать на первом рендере, до выставления
`_columnsReady`/`StateHasChanged`:

```csharp
if (firstRender)
{
    if (Dynamic)
        await InitClientOffset();

    _columnsReady = true;
    _dataKey++;
    StateHasChanged();
}
```

Проверь по коду, что cell-шаблоны в `InitDynamicMode` читают `_clientOffset` **из замыкания в
момент рендера ячейки**, а не копируют его в локальную переменную при сборке шаблона. Сейчас
`_clientOffset` захватывается как поле (`this._clientOffset`) — обновление поля подхватится.
Если это не так — ОСТАНОВИСЬ и спроси, не переписывай сборку шаблонов самостоятельно.

## Не делай

Не вызывай JS из `OnInitializedAsync`/`InitDynamicMode` — пререндер упадёт. Не отключай
пререндер ради этого. Не используй `TimeZoneInfo.Local` на сервере — это пояс сервера, а не
пользователя. Не меняй `ClayDateTimeConverter` — он чистый и уже принимает смещение явно.
Не трогай Тип 3 (`Date`) — он без конвертации по спецификации.

## Проверка

- Юнит (TG8, уже есть на `ClayDateTimeConverter`): `ConvertFromUtc(new DateTime(2026,1,1,21,0,0, DateTimeKind.Utc), TimeSpan.FromHours(3))`
  → `2026-01-02 00:00`; `Kind != Utc` → значение возвращается без изменений.
- Ручная: грид с колонкой Тип 10, значение в БД `2026-01-01 21:00` UTC, браузер в UTC+3 →
  в ячейке `02.01.2026 00:00`, а не `01.01.2026 21:00`;
- сменить часовой пояс ОС на UTC-5, перезагрузить → `01.01.2026 16:00`;
- колонка Тип 13 (`TimeLocal`) с форматом `HH:mm` конвертируется так же;
- Тип 3 (`Date`) значение не сдвинулось;
- в консоли браузера нет ошибок про отсутствующий `clayGridTimeZone`; на серверном
  пререндере в логе нет `InvalidOperationException` про JS interop.
