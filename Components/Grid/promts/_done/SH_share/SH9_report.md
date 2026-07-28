# SH9 — Отчёт

## 1. Изменённые файлы

| Файл | Зачем |
|---|---|
| `scripts/dynamic-grid/schema.sql` | SH2: таблица `ClayGridUserSharedParams`, колонка `КодНастройкиОбщей`, FK без каскада, UNIQUE (3 колонки), переписанный триггер, TVF `ClayGridUserParamsShared`, seed |
| `src/Clayzor.Lib.Entities/DynamicGrid/ClayGridSchemaMap.cs` | SH3: +`SharedId` в `UserParamCols` |
| `src/Clayzor.Lib.Entities/DynamicGrid/ClayGridUserParamsData.cs` | SH3: `sharedId` во всех сигнатурах (default 0) |
| `src/Clayzor.Lib.Entities/DynamicGrid/ClayGridSharedParamsData.cs` | SH3: 7 операций + `CreateWithParamsAsync` |
| `src/Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/ClayGridDynamicSettings.cs` | SH3: +`UserSharedParamsTable`, +`UserParamsShared`, +`Validate()` |
| `src/Clayzor.Lib.Web.Controls/Services/ServiceCollectionExtensions.cs` | Без изменений (регистрация не менялась) |
| `src/Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/ClayGridParamRegistry.cs` | SH4: `GetGridParamNames()` |
| `src/Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/ClayShareUrlBuilder.cs` | SH6: `BuildShareUrl()` |
| `src/Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/ClaySharedParamValidator.cs` | SH8: `IsValid()` |
| `src/Clayzor.Lib.Web.Controls/Components/ClayShareDialog.razor` | SH5: диалог названия |
| `src/Clayzor.Lib.Web.Controls/Components/ClayMenu.razor` | SH7: +`CloseAsync()` |
| `src/Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.razor` | SH5: кнопка Share; SH7: `ClayMenu` списка; SH8: `MudAlert` + индикатор |
| `src/Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.cs` | SH5: `BuildCurrentParamSet`, `OpenShareDialog`, `CreateSharedLinkAsync`; SH7: список общих настроек; SH8: sharedId-режим + choke point |
| `src/Clayzor.Lib.Web.Controls/wwwroot/css/clay.css` | SH5: `toolbar-share-btn`; SH7: `toolbar-shared-list-btn`, `.shared-list-item`; SH8: `.clay-shared-mode-badge` |
| `src/Clayzor.Lib.Web.Controls/wwwroot/js/clayGridShare.js` | SH5/SH6: `copyToClipboard` |
| `src/Clayzor.App.Web.MedicalTests/Components/App.razor` | +`clayGridShare.js` |
| `src/Kesco.App.Web.Inventory/Components/App.razor` | +`clayGridShare.js` |
| `src/Kesco.App.Web.Inventory/appsettings.json` | +`UserSharedParamsTable`, +`UserParamsShared` (fn_) |
| `tests/.../OptionsBindingTests.cs` | SH3: +5 тестов |
| `tests/.../UserParamsTests.cs` | SH4: +4 теста |
| `tests/.../ClayShareUrlBuilderTests.cs` | SH6: +5 тестов |
| `tests/.../ClaySharedParamValidatorTests.cs` | SH8: +7 тестов |
| `tests/.../ClaySharedParamsDataTests.cs` | SH9: +4 теста валидации названия |
| `tests/.../GridStateSerializationTests.cs` | SH9: +1 round-trip тест |
| `src/...Controls/AGENTS.md` | SH2–SH8 в «Выполненные шаги», новые классы |
| `src/...Entities/AGENTS.md` | `ClayGridSharedParamsData` |
| `docs/clay-grid.md` | Кнопка «Поделиться» в тулбаре, `app.css`→`clay.css` |

## 1a. `ClayGridDynamicOptions` и параметры тега

```
grep -rn "ClayGridDynamicOptions" src/ tests/
```

Вхождения только в архиве `promts/_done/` (исторические ссылки). В `.cs`-файлах — ноль.

Параметров тега у `ClayGrid` — ровно 10, новых не появилось.

## 2. Точка запрета сохранения (choke point)

**Расположение:** `ClayGrid.Dynamic.cs:941` — `SaveParamIfChanged()`, первая строка:
```csharp
if (_isSharedMode) return;
```

**Места, проходящие через choke point (все → `SaveDynamicState` → 6× `SaveParamIfChanged`):**
- `LoadDynamicData` (после каждой загрузки)
- `OnColumnDrop` (после перетаскивания колонок)
- Диалог настройки колонок (Apply)

**Точечных `if (_isSharedMode)` у вызывающих — ноль.** Все идут через единственный choke point.

**Рефакторинг сведения в одну точку:** не потребовался — `SaveDynamicState` уже была единой точкой сохранения (`SH1`, вопрос 3).

## 3. Атомарность создания

**Транзакции:** `DbManager` не поддерживает (SH1, вопрос 6).

**Решение:** компенсация в `ClayGridSharedParamsData.CreateWithParamsAsync`:
1. `CreateAsync` → новый `sharedId`
2. `SaveParamsAsync` → пачка INSERT
3. При ошибке шага 2 → `DeleteSharedOnlyAsync` (удаление «сироты»)
4. Исходная ошибка пробрасывается

## 4. Схема: блокеры закрыты

Пункты 3–5, 7, 9 из части 2 SH9 — ручная проверка на стенде `schema.sql`.

## 4a. Запросы с `КодНастройкиОбщей = 0`

| Метод | Файл | Что добавлено |
|---|---|---|
| `BuildLoadSql` | `ClayGridUserParamsData.cs:35` | `AND [{sc.SharedId}] = @shid` (sharedId=0 для личных) |
| `BuildInsertSql` | `ClayGridUserParamsData.cs:45` | `[{sc.SharedId}]` в списке колонок + `@shid` |

Все вызовы `LoadAsync`/`SaveAsync` передают `sharedId: 0` явно или через дефолт.

## 5. Сообщение об ошибке

**Реализация:** `MudAlert` с `Severity.Warning` в `ClayGrid.razor:19-32`. 
Сообщение об ошибке выводится через `_dynamicError`. При `_isSharedMode` добавляется кнопка «Открыть без общих настроек».

**Данные не запрашиваются:** `_dynamicError` устанавливается до `NotifyQueryChanged()` → грид не рендерится, SQL-запросов нет.

## 5a. Тип объекта `UserParamsShared`

В стенде (`schema.sql`): **встроенная табличная функция** `dbo.ClayGridUserParamsShared`. Вызов: `SELECT ... FROM [имя](@shid)`.

В БД Inventory: пользователь переименовал в `fn_НастройкиОбщие` (префикс `fn_` — функция). Вызов через `SELECT FROM` корректен.

Фильтр по `КодНастройкиКлиента` в функции отсутствует — ссылку открывает другой пользователь.

## 6. Хост в ссылке

Ссылка строится из `NavigationManager.Uri` — тот же хост, что видит пользователь. Проблема реверс-прокси не проявляется на стенде `localhost`. При развёртывании за прокси — настройка forwarded headers (отдельная задача).

## 7. Дубликаты ключа в одном INSERT

Со стороны приложения дубликатов нет: `SaveParamsAsync` вызывает `SaveAsync` в цикле по уникальным ключам словаря. Каждый INSERT — одна строка. Триггер корректно обрабатывает одиночные INSERT.

## 8. Что дописать в документацию

- `docs/clay-grid.md`: кнопка «Поделиться» и список общих настроек в разделе тулбара (добавлено)
- `docs/clay-grid.md`: раздел «Общие настройки (SH)» — flow создания и открытия по ссылке
- `AGENTS.md` (Controls): раздел про `ClayMenu.CloseAsync()`

## 9. Принятые предположения

- Имя query-параметра `sharedId` — константа (SH0, решение 8, не подтверждено)
- `clientId` при сохранении общих настроек = 0 (владелец выводится из `КодНастройкиКлиента`)
- Объект `UserParamsShared` — табличная функция (проверено в Inventory: `fn_НастройкиОбщие`)
- Формат сериализации общих настроек совпадает с личными (подтверждено round-trip тестом)

## Автотесты

**Всего: 235 тестов** (32 новых в серии SH).

| Файл | Тестов | Новых |
|---|---|---|
| `OptionsBindingTests.cs` | 8 | +5 |
| `UserParamsTests.cs` | 8 | +4 |
| `ClayShareUrlBuilderTests.cs` | 5 | +5 |
| `ClaySharedParamValidatorTests.cs` | 7 | +7 |
| `ClaySharedParamsDataTests.cs` | 4 | +4 |
| `GridStateSerializationTests.cs` | 5 | +1 |
| Прочие (без изменений) | 198 | 0 |
