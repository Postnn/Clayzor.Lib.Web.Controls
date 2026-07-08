# 14. Выбор колонок перед печатью/экспортом (отдельно от вида грида на странице)

Перед выполнением любой из групповых операций печати или выгрузки в Excel
(меню «Групповые операции» → «Печать» / «Выгрузка в Excel» в `ClayGrid.razor`,
обработчики в `ClayGrid.ExportMenu.cs`) — спрашивать пользователя, нужно ли
настроить состав и порядок колонок именно для этого вывода. Настройка не должна
влиять на отображение грида на странице (`_columnOrder`, `_hiddenSqlNames`,
`_sortState` не трогаем) — она влияет только на конкретный печатаемый/выгружаемый
документ.

Зависит от существующих: `ClayColumnSettingsDialog.razor`, `ColumnSettingsItem.cs`,
`IClayGrid.GetVisibleColumns()`, `ClayGrid.ExportMenu.cs`.

## Точки входа (6 штук, все в `ClayGrid.ExportMenu.cs`)
`PrintCurrentPageInternal`, `PrintSelectedInternal`, `PrintAllInternal`,
`ExcelCurrentPageInternal`, `ExcelAllInternal`, `ExcelSelectedInternal`.

Сейчас каждый из них берёт колонки строкой
`var columns = ((IClayGrid)this).GetVisibleColumns();`. Эту строку нужно заменить
вызовом нового общего метода, который сначала спрашивает пользователя, а затем
либо возвращает `GetVisibleColumns()` как раньше, либо — результат диалога
настройки, либо `null` (операция отменена пользователем — метод должен сделать
`return` без печати/выгрузки).

## 1. Вопрос пользователю (3 исхода, не 2)
Нужно различать три исхода: «настроить», «печатать/выгружать как на странице»,
«отменить всю операцию». `ConfirmDialog.razor` для этого не подходит по смыслу
(иконка `WarningAmber` + кнопка `Color.Error` — стиль предупреждения об опасном
действии, а тут нейтральный вопрос с 3 вариантами, не 2).

Сделать новый лёгкий диалог `Components/Grid/ClayColumnSettingsPromptDialog.razor`:
- `MudDialog` + `[CascadingParameter] IMudDialogInstance MudDialog` (как везде в проекте,
  не `@bind-Visible`);
- параметр `[Parameter] public string ContextLabel { get; set; }` — что печатаем/выгружаем,
  например «печати (все данные)» или «выгрузки в Excel (выбранные записи)»; подставляется
  в текст вопроса;
- три кнопки в `DialogActions`:
  - «Настроить колонки» → `MudDialog.Close(DialogResult.Ok(true))`
  - «Как на странице» → `MudDialog.Close(DialogResult.Ok(false))`
  - «Отмена» → `MudDialog.Cancel()`
- открывается через `DialogService.ShowExAsync<ClayColumnSettingsPromptDialog>(...)`
  с `DialogOptionsEx { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true }` — тем же
  паттерном, что и `ClayColumnSettingsDialog`.

## 2. `ClayColumnSettingsDialog.razor` — режим без сортировки
Добавить параметр:
```csharp
[Parameter] public bool ShowSorting { get; set; } = true;
```
Когда `false`:
- зона `sort-toggle-area` не кликабельна и не показывает бейдж сортировки
  (`GetDialogSortBadge` не вызывается / `@onclick` не навешивается, курсор обычный,
  не `pointer`);
- кнопка «Сбросить сортировку» в `DialogActions` не рендерится;
- `_dialogSortState` не используется, `SortPriority`/`IsSortDesc` в результирующих
  `ColumnSettingsItem` игнорируются вызывающим кодом.

Заголовок диалога (`ShowExAsync("...", ...)`) при вызове из печати/экспорта должен
явно называть контекст: «Колонки для печати» / «Колонки для выгрузки в Excel»
(не переиспользовать текст «Настройка колонок» из `OpenColumnSettings`, чтобы не
путать со страничными настройками).

Не менять поведение по умолчанию (`ShowSorting = true`) — вызов из
`OpenColumnSettings()` в `ClayGrid.razor.cs` не трогаем.

## 3. Источник списка колонок для диалога
В `OpenColumnSettings()` уже есть код построения `List<ColumnSettingsItem>` из
`_columnBySqlName` / `_columnOrder` / `_hiddenSqlNames` / `IsGrouped`. Вынести его
в приватный helper, например:
```csharp
private List<ColumnSettingsItem> BuildColumnSettingsItems()
```
и переиспользовать в `OpenColumnSettings()` и в новом коде — не дублировать логику.

Стартовое состояние для диалога печати/экспорта = **текущее отображение колонок на
странице** (тот же набор, что вернул бы `BuildColumnSettingsItems()`), это просто
удобная отправная точка. Колонки, участвующие в группировке (`IsGrouped(sqlName)`),
по-прежнему исключаются/не выбираемы — они не являются обычными колонками вывода
ни на странице, ни в печати/экспорте.

Важно: работать нужно с **копией** — как и в `ClayColumnSettingsDialog.OnInitialized`,
никакие правки в диалоге не должны попадать в `_columnOrder` / `_hiddenSqlNames`
самого грида.

## 4. Общий метод разрешения колонок
В `ClayGrid.ExportMenu.cs` добавить:
```csharp
private async Task<IReadOnlyList<ClayColumnMeta>?> ResolveExportColumnsAsync(string contextLabel)
```
Логика:
1. Открыть `ClayColumnSettingsPromptDialog` с `ContextLabel = contextLabel`.
2. Результат `Canceled` → вернуть `null`.
3. Результат `false` («как на странице») → вернуть `((IClayGrid)this).GetVisibleColumns()`.
4. Результат `true` («настроить») → построить `BuildColumnSettingsItems()`, открыть
   `ClayColumnSettingsDialog` с `ShowSorting = false` и подходящим заголовком.
   - Если пользователь отменил диалог настройки — вернуть `null` (вся операция отменяется,
     не откатываться на «как на странице» молча).
   - Если применил — из результирующего `List<ColumnSettingsItem>` взять элементы с
     `IsVisible == true` **в их порядке**, разрешить каждый через `_columnBySqlName`
     и вернуть как `IReadOnlyList<ClayColumnMeta>`.
   - Если пользователь снял видимость со всех колонок — не давать применить пустой
     результат: заблокировать кнопку «Применить» в `ClayColumnSettingsDialog` (или
     показать `Snackbar` с ошибкой и не закрывать диалог), т.к. пустой список колонок
     означает документ без единой колонки.

Каждый из 6 методов (`PrintCurrentPageInternal` и т.д.) заменяет
`var columns = ((IClayGrid)this).GetVisibleColumns();` на
```csharp
var columns = await ResolveExportColumnsAsync("<контекст>");
if (columns is null) return;
```
где `<контекст>` — короткая подпись действия, например:
- Печать / текущая страница → `"печати (текущая страница)"`
- Печать / выбранные → `"печати (выбранные записи)"`
- Печать / все данные → `"печати (все данные)"`
- Excel / текущая страница → `"выгрузки в Excel (текущая страница)"`
- Excel / выбранные → `"выгрузки в Excel (выбранные записи)"`
- Excel / все данные → `"выгрузки в Excel (все данные)"`

Остальной код методов (спиннер, вызов `DataLoader.BuildPrintHtmlAsync` /
`BuildPrintHtmlForSelectedAsync` / `ExcelExportRequest`, обработка ошибок) не меняется —
он уже принимает `IReadOnlyList<ClayColumnMeta>` в нужном порядке и ничего не знает,
как этот список был получен.

## 5. Что не трогаем
- SQL-запросы (`BuildAllRowsForPrint`, `BuildExportRows` и т.п.) — они всегда делают
  `SELECT *`, список колонок влияет только на то, что рендерится в HTML/Excel, а не
  на то, что выбирается из БД. Менять не нужно.
- `clayColumnSettings.js`, `clayGridPrint.js`, `clayGridExcel.js` — без изменений,
  тот же drag-and-drop переиспользуется для диалога с `ShowSorting=false`.
- Кастомные `BatchOperationGroup`/`BatchOperation` (пользовательские операции) — этот
  промт их не касается, там нет встроенной концепции «колонок».

## Критерии
- [ ] Перед каждым из 6 действий печати/экспорта показывается вопрос с тремя исходами.
- [ ] «Как на странице» → поведение идентично текущему (`GetVisibleColumns()`).
- [ ] «Настроить» → диалог колонок без сортировки; порядок и видимость не совпадают
      обязательно с гридом на странице и не изменяют его состояние.
- [ ] Отмена вопроса ИЛИ отмена диалога настройки → операция печати/экспорта не
      выполняется вообще (без спиннера, без файла, без печати).
- [ ] Нельзя применить настройку с нулём видимых колонок.
- [ ] `_columnOrder`, `_hiddenSqlNames`, `_sortState` страницы грида не изменяются
      в результате настройки колонок печати/экспорта.
- [ ] `OpenColumnSettings()` (обычная настройка колонок страницы) работает как раньше,
      использует тот же вынесенный `BuildColumnSettingsItems()`.
- [ ] `dotnet build` без ошибок.
