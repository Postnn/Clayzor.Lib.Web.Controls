> Глобальные правила и обзор решения — в корневом /AGENTS.md. Здесь — только специфика проекта Clayzor.Lib.Web.Controls.

## Карта подсистем

| Подсистема | AGENTS.md |
|---|---|
| **ClayGrid** — грид, динрежим, группировка, фильтрация, codebehind | [`Components/Grid/AGENTS.md`](Components/Grid/AGENTS.md) |
| **ClayTreeView** — дерево, заплатки CTF, кейсет-пагинация CTP | [`Components/Tree/AGENTS.md`](Components/Tree/AGENTS.md) |
| **ClayFilter** — общий настраиваемый фильтр (дерево И/ИЛИ), серия CF | [`Components/Filter/AGENTS.md`](Components/Filter/AGENTS.md) |

## Shared components

| Компонент | Документация |
|---|---|
| **ClayGrid\<T>** — грид с серверной пагинацией, поиском, сортировкой, группировкой, фильтрацией по колонкам. Разметка в `ClayGrid.razor`, логика в partial class-файлах. Конфигурация — `ClayGridOptions` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGridPageBase\<T>** — базовый класс страниц с гридом. Предоставляет `LoadData`, `ToggleGroup`, `OpenAddDialog` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumn\<T>** — колонка грида с авто-заголовком | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumnDef** — невидимый регистратор метаданных колонки | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGroupHeader** — заголовок строки группы | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayDragState** — статическое хранилище SQL-имени перетаскиваемой колонки | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGridPrintHtmlGenerator** — генератор HTML для печати | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayGridPrintStyles** — символы для печатных форм | — |
| **ClayColumnFilterDialog** — диалог настройки фильтра по колонке (гридовый, в `Components/Grid/`) | [docs/clay-column-filter-dialog.md](docs/clay-column-filter-dialog.md) |
| **ClayColumnValueFilterDialog** — диалог фильтра по уникальным значениям (гридовый) | [docs/clay-grid.md](docs/clay-grid.md) |
| **OpenConditionRequest** — record для маршрутизации из диалога значений в форму условия | — |
| **ClayFilterDialog** — диалог настраиваемого фильтра (общий, `Components/Filter/`) | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterExpression** — редактор листового условия составного фильтра (`Components/Filter/`) | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterGroup** — рекурсивный узел-группа составного фильтра (`Components/Filter/`) | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterValueEditor** — единый редактор значения фильтра (`Components/Filter/`) | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayFilterColumnInfo** — описание фильтруемого поля (контракт диалога, `Components/Filter/`) | — |
| **ClayFilterDescriptionBuilder** — статический построитель сегментов/текста фильтра (`Components/Filter/`) | — |
| **ClayFilterJsonConverter** — `JsonConverter<IClayFilterNode>` (`Components/Filter/`) | — |
| **ClayFilterUrlHelper** — дерево → JSON → DeflateStream → Base64Url (`Components/Filter/`) | — |
| **ClayFilterStrings** — единый источник строковых констант UI фильтра (`Components/Filter/`) | — |
| **ClayFilterOperatorLabels** — читаемые русские метки операторов (`Components/Filter/`) | — |
| **ClayCompositeSqlBuilder** — статический SQL-билдер фильтра (`Components/Filter/`) | — |
| **ClayFilterOption** — вариант для выпадающего списка значения (`Components/Filter/`) | — |
| **FilterSegment** — кликабельный сегмент в панели фильтра (`Components/Filter/`) | — |
| **DistinctValuesResult** — результат `LoadDistinctValuesAsync` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumnSettingsDialog** — диалог настройки порядка, видимости, сортировки колонок | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumnSettingsPromptDialog** — лёгкий диалог с тремя исходами перед печатью/экспортом | — |
| **ClayEditForm\<T>** — MudDialog с валидацией, сохранением, удалением | [docs/clay-edit-form.md](docs/clay-edit-form.md) |
| **ClayComboBox\<TItem>** — выпадающий список для `ILookupEntity` | [docs/clay-combo-box.md](docs/clay-combo-box.md) |
| **ClayErrorBar** — баннер ошибок БД с детализацией | [docs/clay-error-bar.md](docs/clay-error-bar.md) |
| **ClayDbReconnectOverlay** — оверлей при недоступности SQL Server | — |
| **ClayButton** — обёртка `MudTooltip` + `MudIconButton` с авто-сбросом тултипа | — |
| **ClayMenu** — обёртка `MudMenu` с авто-построением кнопки-активатора | — |
| **ClayCheckbox** — контролируемый чекбокс с tri-state поддержкой | — |
| **ConfirmDialog** — диалог подтверждения | [docs/confirm-dialog.md](docs/confirm-dialog.md) |
| **ClayShareDialog** — диалог ввода названия общей настройки | — |
| **ILookupEntity** — интерфейс справочной сущности | [../Clayzor.Lib.Entities/docs/entity-crud.md](../Clayzor.Lib.Entities/docs/entity-crud.md) |
| **ClayTheme** — corporate theme (DarkNavy + Gold accent) | — |
| **ClayColors** — public C# константы для brand-цветов | — |

## Интерфейсы

| Интерфейс | Назначение |
|---|---|
| **IClayGrid** — контракт ClayGrid: `Options`, `ToggleSort`, `GetSortBadge`, `GetColumnMeta`, группировка, фильтрация, регистрация колонок | [docs/clay-grid.md](docs/clay-grid.md) |
| **IClayGridDataLoader** — контракт обратного вызова: `OnQueryChangedAsync`, `ExcelExportAsync`, `BuildPrintHtml*`, `LoadDistinctValuesAsync` | [docs/clay-grid.md](docs/clay-grid.md) |
| **ClayColumnMeta** — метаданные зарегистрированной колонки | [docs/clay-grid.md](docs/clay-grid.md) |
| **IClayGridCellReader** — абстракция чтения ячейки для генераторов печати и Excel | — |

## Настройки компонентов (Options)

**Правила** — обязательны для всех новых компонентов библиотеки:

1. Конфигурация компонента живёт в отдельном классе `Clay<Имя>Options`, рядом с компонентом. Класс `sealed`, POCO с `{ get; set; }`, дефолты в объявлении свойств, `/// <summary>` на каждом свойстве.
2. Параметрами тега (`[Parameter]`) остаются только: данные, `RenderFragment`, `EventCallback`, живые ссылки на потребителя, служебные параметры для `@ref`.
3. Объект настроек создаётся страницей **один раз** — поле + `OnInitialized`, не выражением в разметке.
4. Компонент внутри читает **единственное** поле действующих настроек (`_opt`).
5. Дефолты свойств — единственный источник значений по умолчанию.
6. Ничего, что компонент **записывает** сам в процессе работы, в options не кладётся.
7. Настройки уровня приложения (из `appsettings`/`web.config`, через DI) — **другой** класс (`*Settings`).

Правило именования: **`*Settings`** — настройки уровня приложения (через DI); **`*Options`** — настройки одного экземпляра компонента на странице.
Образец — [`docs/component-options-template.md`](docs/component-options-template.md).

## Services

| Сервис | Назначение |
|---|---|
| **ClayErrorService** (Scoped) — хранит состояние последней ошибки SQL, реализует `ISqlErrorHandler` |
| **ISqlErrorHandler** (DALC) — интерфейс, вызываемый `DbManager` при `SqlException` |
| **ClayDbReconnectService** (Scoped) — авто-переподключение к SQL Server (3×30s health-check) |
| **ClayReflectionCellReader** — читает значение ячейки через рефлексию |
| **ClayGridExportFileName** — `Sanitize(title)` — убирает недопустимые символы из имени файла |

**Стили компонентов:** общий стиль грида/треев/чипов/диалогов живёт в `wwwroot/css/clay.css`.
