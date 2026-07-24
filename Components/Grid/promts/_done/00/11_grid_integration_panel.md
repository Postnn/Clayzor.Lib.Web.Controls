# 11. Интеграция в ClayGrid: панель и маршрутизация

Панель показывает текст всего дерева; каждое условие — кликабельный сегмент;
клик открывает диалог по происхождению листа. Колоночный фильтр — лист дерева.
Правки идут в `ClayGrid.Filtering.cs` (задача 05).

## Колоночный фильтр → лист дерева
- `OnFilterTrayDrop`/`OpenFilterDialog(sqlName, displayName)`: как сейчас открывают
  `ClayColumnFilterDialog` через `DialogService.ShowExAsync<...>`, получают `ColumnFilter`.
  Новое: проставить `Source = ClayFilterSource.ColumnDialog` и **вставить/заменить**
  лист в дереве по `Column` (есть лист этой колонки → заменить, иначе добавить в корень).
- Редактирование: клик по сегменту колоночного листа → `ClayColumnFilterDialog`
  с `ExistingFilter = <этот ColumnFilter>`.

## Диалог настраиваемого фильтра
- `OpenCompositeFilterDialog()` (объявлен в задаче 10):
  `DialogService.ShowExAsync<ClayFilterDialog>(...)`, передать `_filterRoot.Clone()`,
  список фильтруемых колонок (`GetVisibleColumns()` где `Filterable` + дескрипторы типов +
  необязательные `Options`). Результат → `_filterRoot`, перезагрузить данные.
- Точка входа — иконка `FilterAlt` трея / отдельная кнопка.

## Панель: текст дерева + кликабельные сегменты
- Заменить рендер чипов: рекурсивный обход `_filterRoot` через **единый** построитель
  описания (расширить `ClayColumnFilterDialog.GetFilterDescription` на группы: скобки, И/ИЛИ).
  `BuildFilterDescription()` (печать/экспорт) — на тот же построитель.
- Каждое **условие** (клауза листа) — отдельный кликабельный сегмент. Клик:
  - лист `Source==ColumnDialog` → `OpenFilterDialog(list.Column, displayName)`;
  - иначе → `OpenCompositeFilterDialog()`.

## Критерии
- [ ] Перетаскивание заголовка → колоночный диалог (≤2 условия) → лист `Source=ColumnDialog`.
- [ ] Панель = текст всего дерева; каждое условие кликабельно; маршрут по происхождению.
- [ ] Очистка (пустой корень) убирает фильтрацию во всех путях.
- [ ] `dotnet build` без ошибок; колоночная фильтрация работает как прежде.
