> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненного **GF2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF10 — сообщение «грид не найден» вместо пустой страницы

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.cs` — `InitDynamicMode`
(ранние выходы), `ResolveDynamicGridId`; `Components/Grid/ClayGrid.razor` — начало разметки
(`<MudPaper>`, заголовок, тулбар); `Components/ClayErrorBar.razor` и
`Services/ClayErrorService.cs` — какой API реально есть; `Kesco.App.Web.Inventory/Components/Layout/MainLayout.razor`;
`promts/G04_dynamic_render.md` — исходное требование.

## Дефект

G4 требовал: «если null — показать сообщение „грид не найден“». Фактически:

```csharp
var gridId = ResolveDynamicGridId(opt);
if (gridId == 0) return;
...
_dynamicDef = await ClayGridDefinitionData.LoadGridAsync(...);
if (_dynamicDef is null) return;
```

Оба выхода молчаливые. Пользователь получает страницу с тулбаром, заголовком «Список» (значение
`Title` по умолчанию) и пустым гридом — неотличимо от «данных нет». Два сценария, оба живые:

- открыли `/` без `?id=` → `gridId == 0`;
- в `?id=` код запроса, которого нет в `Запросы` → `_dynamicDef is null`.

`ClayErrorService` для этого не подходит: у него единственная точка входа
`HandleSqlError(SqlException, ...)`, он про ошибки SQL, и `ClayErrorBar` в `MainLayout`
приложения Inventory не подключён. Сообщение показывает сам грид.

## Изменить/создать

**1.** `ClayGrid.Dynamic.cs` — поле и заполнение на ранних выходах:

```csharp
/// <summary>Текст ошибки инициализации динамического режима (грид не найден и т.п.).</summary>
private string? _dynamicError;

private async Task InitDynamicMode()
{
    var opt    = DynamicOpts.Value;
    var gridId = ResolveDynamicGridId(opt);

    if (gridId == 0)
    {
        _dynamicError = $"Не указан код запроса: ожидается query-параметр «{opt.GridIdQueryParam}».";
        return;
    }

    _dynamicGridId = gridId;
    _dynamicClid   = ResolveClientId(opt);

    _dynamicDef = await ClayGridDefinitionData.LoadGridAsync(Db, gridId, opt.SettingsTable, opt.Schema);
    if (_dynamicDef is null)
    {
        _dynamicError = $"Грид не найден: запрос №{gridId} отсутствует в «{opt.SettingsTable}».";
        return;
    }

    /* … как было … */
}
```

**2.** `ClayGrid.razor` — показать сообщение вместо содержимого грида. Внутри существующего
`<MudPaper ...>`, сразу после открывающего тега, обернуть остальное:

```razor
@if (_dynamicError is not null)
{
    <MudAlert Severity="Severity.Warning" Class="clay-grid-error">@_dynamicError</MudAlert>
}
else
{
    @* … существующая разметка: тулбар, лотки, MudDataGrid, пагинатор … *@
}
```

Тулбар, лотки группировки/фильтрации и пагинатор при ошибке не нужны — показывать их не над чем.

**3.** Класс `clay-grid-error` — в `wwwroot/css/app.css` рядом с остальными `clay-grid-*`,
по STYLE_RULES.md (класс, не инлайн). Достаточно отступов.

## Не делай

Не трогай `ClayErrorService`/`ClayErrorBar` и не добавляй им новый API — это отдельная задача.
Не используй `Snackbar` — сообщение должно быть постоянным на странице, а не всплывающим.
Не бросай исключение из `InitDynamicMode`. Не трогай статический режим: при `Dynamic == false`
`_dynamicError` всегда `null` и разметка идёт прежней веткой.

## Проверка (ручная)

- `/` без `?id=` → предупреждение «Не указан код запроса: ожидается query-параметр „id“»,
  тулбара и пагинатора нет, ошибок в консоли нет;
- `?id=999999` (нет такого в `Запросы`) → «Грид не найден: запрос №999999 отсутствует в „Запросы“»;
- `?id=140` → грид работает как раньше, никакого алерта;
- `<ClayGrid Dynamic="true" DynamicGridId="140" />` без query-параметра → грид работает
  (`DynamicGridId` имеет приоритет, см. `ResolveDynamicGridId`);
- статический режим (`MedicalTests.razor`) не изменился.
