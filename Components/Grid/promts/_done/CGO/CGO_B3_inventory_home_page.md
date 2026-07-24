> Часть серии **CGO**. Перед началом прочитай **CGO0_README_grid_options.md**.
> Требует выполненного **B1** (и по порядку — **B2**, чтобы эталон разметки уже существовал).
> Делай ТОЛЬКО этот шаг.

# B3 — динамическая страница (`Kesco.App.Web.Inventory`) на `ClayGridOptions`

Самый простой шаг серии: страница — 13 строк, из них грид — 8 атрибутов, и все они
конфигурационные (MOVE). Именно поэтому здесь хорошо видно, ради чего всё делалось.

## Прочитать

- `src/Kesco.App.Web.Inventory/Components/Pages/Home.razor` — целиком;
- итог шага **B2** (`MedicalTests.razor`) — эталон, которому нужно соответствовать;
- `Components/Grid/ClayGrid.Dynamic.cs` — `ResolveDynamicGridId`, `InitDynamicMode`: как
  читаются `Dynamic` и `DynamicGridId` (после A3 — `_opt.Dynamic`, `_opt.DynamicGridId`);
- `src/Kesco.App.Web.Inventory/Components/_Imports.razor` — есть ли `@using` для
  `Clayzor.Lib.Web.Controls.Components.Grid` (без него `ClayGridOptions` в `@code` не
  разрешится; если нет — добавь именно туда, а не `@using` в странице).

## Что сделать

Было:

```razor
<ClayGrid TEntity="IClayGridRow"
          Dynamic="true"
          Id="clay-grid-root"
          ShowPagination="true"
          ColumnMenuMode="@ColumnMenuMode.Always"
          SelectVisible="true"
          ShowPrint="true"
          ShowExcel="true" />
```

Станет:

```razor
<ClayGrid TEntity="IClayGridRow" Options="_gridOptions" />

@code {
    private readonly ClayGridOptions _gridOptions = new()
    {
        Dynamic        = true,
        Id             = "clay-grid-root",
        ShowPagination = true,
        ColumnMenuMode = ColumnMenuMode.Always,
        SelectVisible  = true,
        ShowPrint      = true,
        ShowExcel      = true,
    };
}
```

Здесь `readonly`-поле с инициализатором допустимо (в отличие от B2): ни одно значение не
зависит от инжектированных сервисов. Если понадобится `AppSettings` — переноси сборку в
`OnInitialized`, как в B2.

`@page`, `@using`, `@inject ClayAppSettings AppSettings`, `<PageTitle>` — **не трогать**:
`AppSettings` используется в `PageTitle`, а не в гриде.

## Особенность динамического режима — проверь, не сломалось

В динамическом режиме грид сам себе и страница: он читает `Dynamic`/`DynamicGridId`, лезет в
БД за определением, восстанавливает состояние пользователя и **пишет в свои же параметры**
`Items`/`TotalCount` (см. `LoadDynamicFlatData`). После A3 чтения идут через `_opt`, а записи
`Items`/`TotalCount` остались как были (это STAY-параметры). Убедись, что в `ClayGrid.Dynamic.cs`
не появилось попыток писать в `_opt` — настройки грид не изменяет:

```
grep -rn "_opt\.[A-Za-z]* *=" src/Clayzor.Lib.Web.Controls/Components/Grid/
```

→ должно быть пусто (единственные присваивания — само `_opt = ResolveOptions()`).

Отдельно: `ResolveDynamicGridId` берёт код запроса из `_opt.DynamicGridId`, а если он не задан —
из query-строки (`?id=`). Страница `DynamicGridId` не задаёт, значит работает вторая ветка.
Проверь, что после правки `_opt.DynamicGridId` — по-прежнему `null`, а не `0`: в
`ClayGridOptions` он объявлен как `int?` (сверь с A2), и подмена на `int` со значением `0`
уронила бы всю логику `ResolveDynamicGridId`.

## Не делай

- Не добавляй на страницу настроек, которых там не было (`EnableValueFilter`, `Title`,
  `PageSize`). Динамический грид берёт заголовок из БД (`Запросы`) — задать `Title` здесь
  значит его перебить.
- Не переводи `Home.razor` на `ClayGridPageBase` и не добавляй `DataLoader` — в динамическом
  режиме загрузчик не нужен (`GE6`), это архитектурное решение, не упущение.
- Не трогай `ClayGridDynamicOptions`, `appsettings.json`, `web.config` и справочник `Запросы`.
- Не меняй `Program.cs`.

## Проверка

- `dotnet build` + `dotnet test` — зелёные;
- в `Home.razor` у тега `<ClayGrid>` ровно два атрибута;
- **полный ручной чек-лист «Динамический режим» из CGO0**, целиком. Особое внимание:
  - `/?id=140` — грид грузится сразу, без «Обновить» (`GF2` не сломан);
  - `/` без `?id=` → сообщение «Не указан код запроса…», а не пустой грид (`GF10`);
  - кнопка «Выбрать записи» есть, выбор и «Выбранные (N)» работают (`GB1`, `GF13`);
  - `ColumnMenuMode.Always` — кнопка меню ⋮ видна в заголовках колонок на десктопе;
  - `Id="clay-grid-root"` доехал: в DevTools у корневого элемента грида этот `id`
    (на нём висят JS-хелперы прокрутки/drag&drop — если `id` потерян, они молча перестанут
    работать, а на глаз это выглядит как «иногда не сохраняется прокрутка»);
  - состояние (колонки, сортировка, группировка, размер страницы, фильтр) сохраняется и
    восстанавливается после ухода со страницы и возврата;
  - печать и Excel во всех режимах;
- статический стенд `/medical-tests` — прогнать чек-лист «Статический режим» (общий код грида).
