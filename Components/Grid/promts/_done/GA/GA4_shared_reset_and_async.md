# GA4 — ApplySharedParams: чужие настройки накладываются поверх личных; проглоченный async

## Контекст

Файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.cs`. Порядок в `InitDynamicMode`:
сначала `RestoreDynamicState(opt)` (личные сохранённые настройки), потом — если открыта чужая
ссылка — `ApplySharedParams(sharedParams, opt)`.

Дефекты:

1. **Нет сброса перед применением shared.** `ApplySharedParams` вызывает `ApplySavedSort`,
   `ApplySavedGroups`, `ApplyColumnsState`, десериализацию фильтра — но каждый из этих методов
   применяет значение **поверх** уже восстановленного личного состояния:
   - `ApplySavedSort` делает `_sortState.Clear()` — ок для сортировки;
   - `ApplySavedGroups` делает `_groupColumns.Clear()` — ок;
   - **но** фильтр: `if (root is not null) _filterRoot = root;` — если в shared фильтра нет,
     остаётся личный фильтр пользователя (чужая ссылка должна давать чистое, воспроизводимое
     состояние, а не микс);
   - быстрый поиск: `ApplySavedQuickSearch` чистит свой набор — ок;
   - размер страницы: применяется только если есть в shared, иначе остаётся личный.
   Итог: открыв чужую ссылку, пользователь видит гибрид своих и чужих настроек — ссылка
   невоспроизводима, что противоречит самой цели «поделиться».
2. **Проглоченный async.** Последняя строка `ApplySharedParams`:
   `_ = RefreshQuickSearchEffective(opt);` — метод `async Task`, запущен без `await`.
   Исключения теряются, порядок с последующей `NotifyQueryChanged` не гарантирован (гонка:
   данные могут загрузиться со старым набором колонок поиска).

Исправление: сделать `ApplySharedParams` асинхронным, привести состояние к дефолту перед
применением чужих значений, дождаться пересчёта поиска.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.cs`

**1.1.** Изменить сигнатуру:
`private void ApplySharedParams(...)` → `private async Task ApplySharedParams(...)`.

**1.2.** В начало метода, перед применением, привести состояние к дефолту определения
(снять личные настройки, восстановленные из `RestoreDynamicState`):

```csharp
// Чужая ссылка должна давать воспроизводимое состояние — снимаем личные настройки,
// накопленные RestoreDynamicState, и применяем ТОЛЬКО то, что есть в shared-наборе.
_sortState.Clear();
_groupColumns.Clear();
_filterRoot = new ClayFilterGroupNode();
_dynamicQuickSearchCols.Clear();
// Колонки и размер страницы — вернуть к дефолту определения
ResetColumnsToDefinitionDefault();   // см. шаг 1.4
_pageSize = _opt.DefaultPageSize > 0 ? _opt.DefaultPageSize : _pageSize;
```

Затем — существующие применения из shared (колонки/сортировка/группы/pageSize/фильтр/поиск)
без изменений.

**1.3.** Заменить финальную строку:
`_ = RefreshQuickSearchEffective(opt);`
на
`await RefreshQuickSearchEffective(opt);`

**1.4.** Добавить приватный помощник сброса колонок к дефолту определения. Он должен
восстановить `_columnOrder` и `_hiddenSqlNames` в состояние, собранное в `InitDynamicMode`
до `RestoreDynamicState`. Для этого в `InitDynamicMode` **зафиксировать снапшот дефолта** сразу
после регистрации всех колонок (до `RestoreDynamicState(opt)`):

```csharp
// снапшот дефолтной раскладки — для сброса при применении shared
_defaultColumnOrder  = _columnOrder.ToList();
_defaultHiddenNames  = _hiddenSqlNames.ToHashSet();
```

и поля рядом с другими динамическими полями:

```csharp
private List<int> _defaultColumnOrder = [];
private HashSet<string> _defaultHiddenNames = [];
```

Сам помощник:

```csharp
private void ResetColumnsToDefinitionDefault()
{
    _columnOrder.Clear();
    _columnOrder.AddRange(_defaultColumnOrder);
    _hiddenSqlNames.Clear();
    foreach (var n in _defaultHiddenNames) _hiddenSqlNames.Add(n);
    _dataKey++;
}
```

**1.5.** Обновить вызов в `InitDynamicMode`:
`ApplySharedParams(sharedParams, opt);` → `await ApplySharedParams(sharedParams, opt);`

(Проверить `_opt.DefaultPageSize`: если такого свойства нет — использовать текущее значение по
умолчанию `_pageSize`, установленное при инициализации; в этом случае строку с `_pageSize`
из 1.2 убрать.)

## Критерии приёмки

- Сценарий: пользователь с личной сортировкой/фильтром открывает чужую ссылку без фильтра →
  видит состояние ссылки (без своего фильтра), а не гибрид.
- Две разные машины/пользователя по одной ссылке видят идентичное состояние грида.
- Нет предупреждений компилятора о незаваершённых `Task`; `RefreshQuickSearchEffective`
  завершается до `NotifyQueryChanged`.
- Существующие shared-тесты (`SH9_tests_manual_report`, `SH8`) проходят.
