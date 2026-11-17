# ClaySelect — одиночный выбор элемента

> Глобальные правила — в `/AGENTS.md`. Контекст библиотеки — в [`../../AGENTS.md`](../../AGENTS.md).
> Пользовательские сценарии — в [руководстве ClaySelect](../../docs/clay-select-user-guide.md).

## Назначение и основной контракт

`ClaySelect<TItem, TValue>` — поле одиночного выбора из статического списка или серверного
источника. Поддерживает явный текстовый поиск, восстановление элемента по ID, клавиатурную
навигацию, валидацию `MudForm`, просмотр выбранной сущности, создание и расширенный поиск.

- `TItem` — ссылочный тип элемента справочника.
- `TValue` — nullable-тип идентификатора: `int?`, `Guid?`, `string` и т. п. Для значимых
  типов nullable-обёртка обязательна; неnullable `TValue` отклоняется в `OnParametersSet`.
- Только `null` означает пустой выбор. Значения `0` и `Guid.Empty` являются допустимыми ID.
- `Value` — источник истины. `SelectedItem` должен соответствовать `Value`; при расхождении
  приоритет остаётся за идентификатором.
- `DataSource` имеет приоритет над `Items`. Без `DataSource` поиск и восстановление выполняются
  по статической коллекции `Items`.
- `ValueSelector` и `TextSelector` обязательны. `ValueSelector` не должен возвращать `null`
  для выбранного элемента.
- Настройки экземпляра находятся в `ClaySelectOptions`; объект создаётся страницей один раз,
  а не выражением в Razor-разметке.

## Синхронизация Value и SelectedItem

Владелец синхронизации — `OnParametersSetAsync` в `ClaySelect.razor.cs`.

1. Изменение `Value` или смена экземпляра `DataSource` отменяет незавершённые поиски и
   восстановления, очищает backup редактирования и запускает синхронизацию объекта.
2. `Value = null` очищает объект и текст. `SelectedItemChanged(null)` вызывается только если
   ранее существовал выбранный объект.
3. Если `SelectedItem` уже соответствует ID, запрос к источнику не нужен.
4. Для статического списка элемент сначала ищется в `Items`.
5. Для серверного источника вызывается `IClaySelectDataSource.GetByIdAsync`.
6. Если элемент не найден, ID сохраняется, объект очищается, а поле показывает технический
   текст `#ID`. Нельзя молча заменять ненайденный ID на `null`.
7. Восстановление объекта по неизменному ID вызывает `SelectedItemChanged`, но не вызывает
   повторный `ValueChanged`.

Повторный рендер родителя с неизменными `Value` и `DataSource` не должен затирать введённый
поисковый текст или запускать лишнее восстановление.

## Поиск и ограничение результатов

Поиск не выполняется на каждый введённый символ. Точки запуска:

- **Enter**, если текущий выбранный текст не совпадает с неизменённым выбранным элементом;
- потеря фокуса — поиск без открытия popup;
- кнопка **«…»** — поиск с открытием или закрытием popup.

`MinSearchLength` проверяется до обращения к источнику. Пустой запрос разрешён при значении 0.

`ClaySelectSearchRequest` содержит `SearchText` и `Limit`. Источник возвращает
`ClaySelectResult<TItem>`:

- `Items` — не более `Limit` видимых элементов;
- `HasMore` — признак дополнительных результатов. Реализации БД обычно запрашивают
  `Limit + 1`, возвращают первые `Limit` и по лишней строке устанавливают `HasMore`.

При `HasMore = true` автовыбор запрещён: уникальность совпадения по полной выдаче не доказана.
`AutoSelectSingle` разрешает автовыбор единственного результата, `AutoSelectExactMatch` —
единственного ID среди точных совпадений текста без учёта регистра. Автовыбор выполняется
только после Enter или blur для непустого запроса, но не кнопкой списка.

## Отмена и защита от устаревших операций

Для поиска и восстановления используются независимые механизмы:

- `_searchVersion` + `_searchCts` — текстовый поиск;
- `_resolveVersion` + `_resolveCts` — восстановление по ID и защита внешних результатов.

После каждого `await` результат применяется только при совпадении версии, отсутствии отмены,
актуальном `Value`/`DataSource`, изменяемом состоянии и неосвобождённом компоненте.
`OperationCanceledException` ожидаемой отмены не является ошибкой UI.

`InvalidatePendingOperations()` отменяет обе группы операций. Он вызывается при внешней смене
значения/источника, новом выборе, очистке и disposal. Поздний ответ старого поиска или
`GetByIdAsync` не должен перезаписывать более новое состояние.

`claySelect.closeOthers` вызывается до открытия popup. Если во время этого JS-вызова компонент
уже закрыт или отменён, поиск не продолжается и popup не появляется.

## Ввод, клавиатура и очистка

- **↓ / ↑** изменяют `_activeIndex` только в открытой загруженной выдаче.
- **Enter** выбирает активную строку либо запускает поиск. Повторный Enter на неизменённом
  выбранном значении не выполняет запрос.
- **Escape** закрывает список и восстанавливает текст; при редактировании Clearable-поля
  используется `_itemBeforeEdit` / `_valueBeforeEdit` для восстановления прежнего выбора.
- **Tab** отменяет поиск и закрывает popup; последующий blur выполняет проверку текста.
- Во время загрузки Enter и стрелки не могут выбрать строку из прежней выдачи.
- Hover мыши не меняет клавиатурный `_activeIndex` и не вызывает рендер компонента.

При первом редактировании текста выбранного Clearable-поля сохраняется backup, затем текущие
ID и объект очищаются. `ClearAsync()` очищает выбор без возможности последующего
восстановления по Escape. При `Clearable = false` редактирование текста не очищает ID.

`Disabled` и `ReadOnly` блокируют все пути изменения: ввод, поиск, выбор, очистку, создание,
расширенный поиск и просмотр. Программная синхронизация параметров при этом сохраняется.

## Валидация и доступность

Компонент наследует `MudFormComponent<TValue, TValue>` и валидирует выбранный ID, а не наличие
произвольного текста. `Required` и `RequiredError` передаются из `ClaySelectOptions`.
Пользовательский выбор, очистка и blur уведомляют `MudForm` и запускают валидацию.

Разметка использует `combobox` / `listbox` / `option`, `aria-expanded`, `aria-controls`,
`aria-activedescendant` и `aria-invalid`. Сообщение об ошибке находится вне
`.clay-select-field`, чтобы не менять якорь popup.

## Внешний поиск, создание и просмотр

`AdvancedSearch` и `CreateItem` получают `ClaySelectAdvancedSearchContext` и возвращают
`ClaySelectExternalResult<TItem, TValue>?`:

- `null` — отмена без изменения выбора;
- `FromItem(item)` — полный объект выбирается сразу;
- `FromValue(id)` — объект восстанавливается через `Items` или `DataSource`.

Если в поле неизменённый выбранный элемент, в контекст передаётся пустой `SearchText`, а не
текст выбора как новый запрос. Результат внешней формы игнорируется после смены значения,
источника, режима редактирования или disposal. После завершения фокус возвращается в поле.

`ViewItem` открывает карточку текущего `_currentItem`, не меняя выбор и текст. При неизменённом
выбранном тексте кнопка поиска заменяется значком просмотра.

### ClaySelectAdvancedSearchService

`Services/ClaySelectAdvancedSearchService` открывает абсолютный URL внешней поисковой формы и
ожидает результат через `postMessage`:

1. Создаётся уникальный `requestId` и `TaskCompletionSource`.
2. JS добавляет к URL `return=1`, `requestId` и `returnOrigin`.
3. Родительское окно принимает сообщение только при совпадении `origin`, `source`,
   типа `clay-select-return` и `requestId`.
4. Выбор завершает ожидание значением; закрытие окна или отмена — `null`.
5. `finally` удаляет pending-запрос и очищает браузерные обработчики. Отмена токена и disposal
   также завершают ожидание и закрывают дочернее окно.

Дочерняя форма возвращает ID через
`claySelectAdvancedSearch.returnValue(requestId, value)`. Сервис регистрируется как Scoped;
JS-скрипт нужен и родительскому окну, и дочерней форме, возвращающей значение.

## JS и жизненный цикл

`wwwroot/js/claySelect.js`:

- `register(inputId, dotnetRef)` устанавливает обработчики outside-mousedown, focusout,
  capture-scroll и resize;
- `closeOthers(exceptInputId)` закрывает остальные экземпляры;
- `positionPopup(inputId)` пересчитывает положение и максимальную высоту, при нехватке места
  открывает список вверх;
- `unregister(inputId)` удаляет все обработчики и запись экземпляра.

Регистрация выполняется в `OnAfterRenderAsync(firstRender)`. При открытом списке после рендера
пересчитывается позиция. `DisposeAsyncCore` отменяет операции, вызывает `unregister` и
освобождает `DotNetObjectReference`; разрыв circuit не должен превращаться в ошибку disposal.

Приложение с `ClaySelect` подключает:

```html
<script src="_content/Clayzor.Lib.Web.Controls/js/claySelect.js"></script>
```

При использовании внешнего поиска дополнительно:

```html
<script src="_content/Clayzor.Lib.Web.Controls/js/claySelectAdvancedSearch.js"></script>
```

## Карта файлов

| Что | Где |
|---|---|
| Разметка combobox, popup, кнопки и ARIA | `ClaySelect.razor` |
| Параметры, синхронизация ID/объекта, восстановление, disposal | `ClaySelect.razor.cs` |
| Поиск, клавиатура, очистка, внешние действия, версии операций | `ClaySelect.Interaction.cs` |
| Настройки экземпляра и русские строки UI | `ClaySelectOptions.cs` |
| Контракт серверного источника | `IClaySelectDataSource.cs` |
| Параметры поиска | `ClaySelectSearchRequest.cs` |
| Результат поиска и `HasMore` | `ClaySelectResult.cs` |
| Контекст внешнего поиска/создания | `ClaySelectAdvancedSearchContext.cs` |
| Результат внешней формы: объект или ID | `ClaySelectExternalResult.cs` |
| Ожидание результата внешнего окна | [`Services/ClaySelectAdvancedSearchService.cs`](../../Services/ClaySelectAdvancedSearchService.cs) |
| Popup и браузерные обработчики | [`wwwroot/js/claySelect.js`](../../wwwroot/js/claySelect.js) |
| Открытие внешнего окна и `postMessage` | [`wwwroot/js/claySelectAdvancedSearch.js`](../../wwwroot/js/claySelectAdvancedSearch.js) |
| Стили `.clay-select-*` | [`wwwroot/css/clay.css`](../../wwwroot/css/clay.css) |
| Руководство пользователя | [`docs/clay-select-user-guide.md`](../../docs/clay-select-user-guide.md) |

## Источники данных и примеры

Готовые реализации находятся в `src/Clayzor.Lib.Web.DBSelect`:

- `Equipment/EquipmentTypeSelectDataSource.cs`;
- `Equipment/EquipmentModelSelectDataSource.cs` — требует выбранный тип и без него не обращается к БД;
- `Equipment/ResourceSelectDataSource.cs` — отдельная строка подключения справочников;
- `Employees/EmployeeSelectDataSource.cs` — поиск по русскому и английскому имени.

Прикладные примеры: `src/Kesco.App.Web.Inventory/Components/Pages/SelectTest.razor` и
`Components/Pages/Equipment/ModelEquipmentForm.razor`. SQL и параметры источников должны
следовать правилам DALC; значения поиска нельзя подставлять в SQL-строку.

## Тесты

Профильные тесты в `tests/Clayzor.Lib.Web.Controls.Tests`:

- `ClaySelectTests` — статический/серверный поиск, лимит, клавиатура, blur, очистка,
  восстановление, stale-ответы, внешние действия, popup и `MudForm`;
- `ClaySelectCreateItemTests` — выбор объекта, возвращённого формой создания;
- `ClaySelectAdvancedSearchServiceTests` — Complete/Cancel, cancellation и disposal;
- `ClaySelectDataSourceTests` — `Limit + 1`, фильтр модели по типу и маппинг сотрудников;
- `ClaySelectInventoryDemoTests` — смена ключа, повторный вход на страницу, disposal страницы
  и поздние внешние результаты.

Запуск из корня суперпроекта:

```bash
dotnet test tests/Clayzor.Lib.Web.Controls.Tests/Clayzor.Lib.Web.Controls.Tests.csproj \
  --filter 'FullyQualifiedName~ClaySelect'
```

## Правила изменений

1. Не нарушать приоритет `Value` и не превращать ненайденный ID в пустой выбор.
2. Не добавлять поиск на каждый символ без отдельного изменения контракта и тестов.
3. Каждую новую асинхронную операцию защищать отменой и проверкой актуальности после `await`.
4. Не вызывать интерактивные обработчики как fire-and-forget; сохранять отмену при disposal.
5. Новые настройки добавлять в `ClaySelectOptions`, а не размножать служебные параметры тега.
6. Проверять все способы изменения значения в `Disabled`/`ReadOnly`, а не только HTML-атрибуты.
7. При изменении поведения обновлять тесты, XML-комментарии и руководство пользователя.
