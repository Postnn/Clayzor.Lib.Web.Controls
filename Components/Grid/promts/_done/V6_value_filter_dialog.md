# V6. Компонент диалога фильтра по значению `ClayColumnValueFilterDialog`

Диалог автофильтра колонки: контекстный список условий сверху, ниже — прокрутка
уникальных значений с галочками, служебные пункты «выбрать все» и «(пустые)»,
асинхронная загрузка с курсором ожидания, обработка порога 100 и взаимоисключение
с фильтром по условию. Покрывает треб. 3–10, 12, 15.

Открытие/возврат — через `IDialogService` + `IMudDialogInstance`, как
`ClayColumnFilterDialog`. Файлы: `Components/Grid/ClayColumnValueFilterDialog.razor`
(+ `.razor.cs`, логика в code-behind).

## Входные параметры
- `[CascadingParameter] IMudDialogInstance? MudDialog`
- `[Parameter] string ColumnSqlName`
- `[Parameter] string ColumnDisplayName`
- `[Parameter] ColumnType ColumnType`  → дескриптор через
  `ColumnTypes.ColumnTypeRegistry.FromKind` (операторы, `Format`).
- `[Parameter] string? BoolTrueLabel`, `[Parameter] string? BoolFalseLabel`
  (треб. 15; для bool-колонки подписывать значения ими, иначе «Да»/«Нет» из
  `BooleanColumnType.Format`).
- `[Parameter] ValueFilter? ExistingValueFilter` — действующий фильтр по значению
  (для восстановления галочек).
- `[Parameter] ColumnFilter? ExistingConditionFilter` — действующий фильтр по
  **условию** этой колонки (`Source=ColumnDialog`), если есть (треб. 8, 9).
- `[Parameter] Func<Task<DistinctValuesResult>> LoadValues` — ленивый загрузчик
  (замыкание на `LoadDistinctValuesAsync` из V4, задаётся в V7). Вызывать в
  `OnInitializedAsync`, **не** раньше открытия (треб. 2).

## Возврат из диалога (`DialogResult.Ok(...)`)
Диалог отдаёт одно из:
- `ValueFilter` — применить фильтр по значению;
- `ValueFilterDialogResult.Cleared` (маленький enum/record) — снять фильтр по
  значению для колонки;
- либо, при клике на условие, диалог закрывается с сигналом «открыть форму
  условия с оператором X» — вернуть `record OpenConditionRequest(ColumnFilterOperator Operator)`;
  маршрутизацию (открыть `ClayColumnFilterDialog` с `InitialOperator=X`) делает V7.
Отмена — `MudDialog.Cancel()`.

## Разметка (3 зоны, фикс. высота)
Обёртка с фиксированной шириной; общая максимальная высота ограничена, внутренняя
зона значений — единственная прокручиваемая (треб. 4).

1. **Шапка — контекстный список условий (треб. 7).** Раскрывающийся пункт
   «Текстовые фильтры» / «Числовые фильтры» / «Фильтры дат» / «—» по типу колонки
   (название по `ColumnType`). Разворачивается в список операторов
   `_descriptor.Operators` с подписями `ClayFilterOperatorLabels.Get(op)`
   (как в `ClayColumnFilterDialog.GetOperatorLabel`). Клик по оператору →
   закрыть диалог с `OpenConditionRequest(op)`.

2. **Блок состояния (условно).**
   - Если `ExistingConditionFilter != null` (треб. 8): показать плашку
     «Установлен фильтр по условию: {описание}» (описание —
     `ClayColumnFilterDialog.GetFilterDescription(ExistingConditionFilter, ColumnDisplayName)`)
     и кнопку-иконку «удалить». Удаление → вернуть сигнал снятия условия
     (например тот же `OpenConditionRequest` не годится — ввести
     `record RemoveConditionRequest()`), маршрут в V7. Пока активен фильтр по
     условию — **список значений заблокирован/скрыт** (треб. 9): галочки не
     показываем, вместо них текст «Фильтр по значению недоступен, пока задан
     фильтр по условию».
   - Если `_result.Capped` (уникальных > 100, треб. 10): под пунктом условий
     показать сообщение «Фильтр по уникальным значениям недоступен: значений
     больше 100». Список значений не рендерить.

3. **Список значений (когда доступен: нет условия и `!Capped`).**
   - Первый пункт **«(Выделить все)»** — **не прокручивается** (sticky сверху,
     вне скролл-контейнера, треб. 6). tri-state: отмечен, если выбраны все
     (значения + пустышки); частично — если часть. Клик — выбрать/снять всё.
   - Прокручиваемый контейнер (`overflow:auto`, `max-height` фикс.) с
     `MudCheckBox` на каждое значение. Порядок значений — **как пришёл из V4**
     (уже отсортирован по алфавиту/возрастанию); в UI не пересортировывать.
     Метка = `_descriptor.Format(value)`; для bool — `BoolTrueLabel`/`BoolFalseLabel`
     при наличии.
   - Последним, **если `_result.HasBlanks`** — пункт **«(пустые)»** с галочкой
     (треб. 6). Внутри прокрутки, в конце.
   - Подвал: `MudButton` «OK» / «Отмена» (как в скриншотах). «OK» собирает
     `ValueFilter` (см. ниже) и `MudDialog.Close(Ok(vf))`. Если снят выбор со
     всего — трактовать по вкусу продукта: либо снятие фильтра (`Cleared`), либо
     запрет OK. Выбрать снятие фильтра (пусто = нет ограничения) и вернуть
     `Cleared`.

## Загрузка (треб. 12)
- Флаг `_loading`; в `OnInitializedAsync`: `_loading=true; StateHasChanged();`
  → `_result = await LoadValues();` → `_loading=false;`.
- Пока `_loading` — показать `MudProgressCircular`/оверлей и курсор ожидания
  (`style="cursor:wait"` на контейнере или `MudOverlay`).
- После загрузки восстановить галочки из `ExistingValueFilter` (учесть `Negate`:
  если `Negate=true`, отмечены все **кроме** `Values`; blank — по `BlankChecked`).

## Сборка `ValueFilter` на OK (в т.ч. инверсия, треб. 14)
Пусть `chosen` — множество отмеченных значений (из `_result.Values`),
`blank` — отмечена ли «(пустые)», `total = _result.Values.Count`.
- `unchosen = total - chosen.Count`.
- Если `chosen.Count == total && (!_result.HasBlanks || blank)` → выбраны все →
  снятие фильтра (`Cleared`).
- Иначе выбрать меньшую сторону:
  - если `unchosen < chosen.Count` → `Negate=true`, `Values = невыбранные`
    (инвертированный запрос `NOT IN`);
  - иначе → `Negate=false`, `Values = выбранные`.
- `BlankChecked`: в режиме IN — `blank`; в режиме NOT-IN семантику пустышек
  задаёт V2 по тому же полю `BlankChecked` (передавать `blank` как есть — таблица
  в V2 покрывает оба режима).
- `Column = ColumnSqlName`, `ParamPrefix = "vf_" + ColumnSqlName`.

## Переиспользование
- Подписи операторов: `ClayFilterOperatorLabels.Get` (см.
  `Filter/ClayFilterOperatorLabels.cs`).
- Формат значений: `ColumnTypeDescriptor.Format`.
- Стиль/плотность списка и `max-height` popover — по образцу `<style>` в
  `ClayColumnFilterDialog.razor` (`.mud-list-item{min-height:32px}` и т.п.).

## Критерии
- [ ] Значения грузятся **только** в `OnInitializedAsync` (лениво), с индикатором
      и курсором ожидания (12).
- [ ] `Capped` → сообщение «недоступно, > 100», список не рендерится (3, 10).
- [ ] «(Выделить все)» — sticky, не прокручивается; «(пустые)» — в конце при
      наличии пустышек (6); каждое значение с чекбоксом (5); высота фиксирована,
      скроллится только зона значений (4).
- [ ] Контекстный список условий по типу колонки; клик → возврат
      `OpenConditionRequest(op)` (7).
- [ ] При активном фильтре по условию: плашка с описанием + удаление, значения
      заблокированы (8, 9).
- [ ] bool-подписи берутся из `BoolTrueLabel`/`BoolFalseLabel` (15).
- [ ] OK собирает `ValueFilter` с выбором меньшей стороны и `Negate` (14);
      «всё выбрано» → снятие фильтра.
- [ ] `dotnet build` без ошибок.
