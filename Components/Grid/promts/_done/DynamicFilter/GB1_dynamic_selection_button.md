> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md**, **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненных **GF13**, **GG8**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB1 — в динамическом гриде нет кнопки «Выбрать записи»

Прочитать перед началом: `Components/Grid/ClayGrid.razor` — тулбар (блок `@if (SelectVisible)`
и `@if (HasBatchOperations)`), колонка выбора (`@key="@SelectColumnKey"`);
`Components/Grid/ClayGrid.razor.cs` — параметры `SelectVisible`, `ShowPrint`, `ShowExcel`,
свойство `HasBatchOperations`; `Components/Grid/ClayGrid.Selection.cs` — целиком;
`Components/Grid/ClayGrid.Dynamic.cs` — `TryGetSelectionId`, `GetRowIdValue`, `InitDynamicMode`
(что именно кладётся в `_dynamicDef`), `_dynamicKnownColumns`;
`Clayzor.Lib.Entities/DynamicGrid/ClayGridDefinition.cs` — поле `IdColumn`;
`src/Kesco.App.Web.Inventory/Components/Pages/Home.razor` — целиком (13 строк);
`src/Clayzor.App.Web.MedicalTests/Components/Pages/MedicalTests.razor` — как параметры
`SelectVisible`/`ShowPrint`/`ShowExcel` передаются в статическом режиме;
`GF13_dynamic_row_selection.md` — раздел «Не делай».

## Дефект

Механика выбора в динамическом режиме уже реализована и работает:
`TryGetSelectionId` (GF13) достаёт ID из колонки `_dynamicDef.IdColumn`, групповые чекбоксы
и `LoadDynamicGroupChildIdsAsync` сделаны в GG8, экспорт «Выбранных» — в GE3/GE5.

Не хватает ровно одного: **режим выбора негде включить.** Кнопка рендерится под флагом:

```razor
@if (SelectVisible)
{
    <ClayButton Text="@(_selectMode ? "Скрыть выбор" : "Выбрать записи")" ... />
}
```

`SelectVisible` — `[Parameter]` без значения по умолчанию (`false`). `MedicalTests.razor`
передаёт `SelectVisible="true"`, а `Home.razor` (единственный хост динамического грида) — нет:

```razor
<ClayGrid TEntity="IClayGridRow"
          Dynamic="true"
          Id="clay-grid-root"
          ShowPagination="true"
          ColumnMenuMode="@ColumnMenuMode.Always"
          ShowPrint="true"
          ShowExcel="true" />
```

Так и было задумано на момент GF13 — там прямо написано: «Не трогай `Home.razor` (там
`SelectVisible` не задан — включи вручную для проверки)». Включить после реализации забыли.

Следствие: `_selectMode` навсегда `false` → сервисная колонка с чекбоксами не рендерится →
`_selectedIds` всегда пуст → пункты «Выбранные (N)» в подменю «Печать» и «Выгрузка в Excel»
(`@if (_selectedIds.Count > 0)`) недостижимы. Кнопка групповых операций при этом есть
(`HasBatchOperations` = `ShowPrint || ShowExcel || CustomBatchGroups`), поэтому со стороны
выглядит как «меню есть, а выбрать нечего».

Второй, скрытый дефект: `SelectVisible="true"` для динамического грида — обещание, которое
грид может не сдержать. `TryGetSelectionId` в динамическом режиме возвращает `false`, если
`Запросы.ID` пуст или значение колонки нечисловое (`int.TryParse`). Тогда кнопка есть, режим
включается, а чекбоксов у строк нет — хуже, чем отсутствие кнопки. Поэтому кнопку в
динамическом режиме показываем только при заполненном `IdColumn`.

## Изменить/создать

**1.** `src/Kesco.App.Web.Inventory/Components/Pages/Home.razor` — включить режим выбора:

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

**2.** `Components/Grid/ClayGrid.razor.cs` — рядом с `HasBatchOperations` завести признак
доступности выбора:

```csharp
/// <summary>
/// Показывать кнопку «Выбрать записи». В динамическом режиме — только если известна
/// колонка первичного ключа (<c>Запросы.ID</c>): без неё TryGetSelectionId всегда даёт
/// false, чекбоксов у строк не будет и режим выбора окажется пустышкой.
/// </summary>
private bool SelectAvailable
    => SelectVisible && (!Dynamic || !string.IsNullOrWhiteSpace(_dynamicDef?.IdColumn));
```

**3.** `Components/Grid/ClayGrid.razor` — заменить условие рендера кнопки:

```razor
@if (SelectAvailable)
```

Разметка кнопки, класс `toolbar-select-btn`, тултипы и `ToggleSelectMode` — без изменений.

**4.** `src/Clayzor.Lib.Web.Controls/AGENTS.md`, раздел про **Selection** — дополнить одной
фразой: в динамическом режиме кнопка появляется только при заполненном `Запросы.ID`.
Больше ничего в документации не трогать (`/AGENTS.md`: актуализация документации — только по
прямому указанию; здесь указание дано ровно на эту фразу).

## Не делай

- **Не меняй значение по умолчанию** `SelectVisible` на `true` — это параметр публичного
  компонента, у статических страниц (`MedicalTests.razor`) режим выбора включается осознанно.
- Не меняй тип `_selectedIds` / `_groupChildIds` / `ExcelExportRequest.SelectedIds` с `int`
  на `string` — грид с нечисловым ID остаётся без выбора, это осознанное ограничение GF13.
  Если такой грид встретится в бою — это отдельная задача, не эта.
- Не трогай `TryGetSelectionId`, `ComputeSelectAllState`, `IsHeaderIndeterminate`,
  `ComputeGroupCheckState`, `LoadDynamicGroupChildIdsAsync` — они рабочие, дефект не в них.
- Не добавляй `SelectVisible` в справочник `Запросы` (новое поле в БД) — это продуктовое
  решение, в объём фикса не входит.
- Не трогай `HasBatchOperations` и состав меню групповых операций.

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`: на панели появилась кнопка «Выбрать записи» (иконка
  `CheckBox`), рядом с «Групповые операции»;
- клик → появилась левая колонка с чекбоксами, кнопка перешла в состояние
  `toolbar-select-btn--active`, тултип «Скрыть выбор»;
- отметить две строки → в меню «Групповые операции» → «Выгрузка в Excel» появился пункт
  «Выбранные (2)»; то же в подменю «Печать»;
- «Выбранные (2)» → диалог выбора колонок → файл выгрузился, в нём ровно две строки;
- чекбокс «выделить всё» в шапке: одна из десяти → indeterminate; все → checked; снять →
  unchecked;
- страница 2 → отметить строку → назад на страницу 1 → отметки сохранились;
- сменить сортировку → выбор сброшен;
- включить группировку (перетащить колонку в трей) → чекбокс на строке-заголовке группы
  выбирает все её записи (tri-state работает);
- выключить режим выбора кнопкой → колонка чекбоксов исчезла, `_selectedIds` очищен, пункты
  «Выбранные (N)» пропали;
- негативный: временно очистить `Запросы.ID` для грида 140 (или указать несуществующую
  колонку) → кнопки «Выбрать записи» нет, грид работает, ошибок в консоли нет. Вернуть
  значение обратно;
- статический режим (`/medical-tests`): кнопка выбора, «выделить всё», групповые операции
  работают ровно как до фикса.
