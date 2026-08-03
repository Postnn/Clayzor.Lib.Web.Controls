# GA6 — Единый разбор query-строки, обработка отмены, удаление устаревшего кода

## Контекст

Три уборочных дефекта.

1. **Троекратный разбор `Nav.Uri`.** `ResolveDynamicGridId`, `ResolveClientId`, `ResolveSharedId`
   каждый делает `new Uri(Nav.Uri)` + `HttpUtility.ParseQueryString`. `ApplyUrlParams` разбирает
   ту же строку ещё раз, но перебором `qs.Keys` — расхождение в обработке регистра/кодирования
   ключей. Один разбор на инициализацию.
2. **`catch { }` глотает отмену и всё подряд.** В `InitDynamicMode` загрузка справочников типов
   5/9 обёрнута в `try { … } catch { /* покажем как есть */ }`; `CreateSharedLinkAsync` — в
   `try { … } catch { }`. `OperationCanceledException` (навигация со страницы во время загрузки)
   маскируется под «справочник не загрузился», ошибки не логируются даже в debug.
3. **Мёртвый `[Obsolete]`-код.** `ClayDataQuery.BuildColumnFilterClause` и словарь
   `ColumnFilters` помечены «упраздняется в задаче 10», обёрнуты `#pragma warning disable CS0618`,
   но живут. Единый источник истины фильтра — `CompositeFilter`. Оставлять оба пути опасно.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.cs`

**1.1. Единый разбор query.** Добавить приватное поле и ленивый разбор:

```csharp
private System.Collections.Specialized.NameValueCollection? _queryCache;

/// <summary>Разобранная query-строка текущего URL. Кешируется на время инициализации.</summary>
private System.Collections.Specialized.NameValueCollection Query
    => _queryCache ??= System.Web.HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query);
```

Переписать три резолвера на использование `Query` вместо собственного разбора:

```csharp
private int ResolveDynamicGridId(ClayGridDynamicSettings opt)
{
    if (_opt.DynamicGridId is { } gid0 && gid0 != 0) return gid0;
    return int.TryParse(Query[opt.GridIdQueryParam], out var gid) ? gid : 0;
}

private int ResolveClientId(ClayGridDynamicSettings opt)
    => int.TryParse(Query[opt.ClientIdQueryParam], out var clid) ? clid : 0;

private int? ResolveSharedId()
{
    var val = Query[ClayShareUrlBuilder.SharedIdParam];
    if (string.IsNullOrEmpty(val)) return null;
    if (!int.TryParse(val, out var sid)) return -1;
    return sid == 0 ? null : sid;
}
```

`ApplyUrlParams` тоже перевести на `Query` вместо локального `qs` (сохранить перебор `Query.Keys`).
Сбросить `_queryCache = null` в конце `InitDynamicMode` не нужно — компонент инициализируется один
раз; но если `Nav` может смениться (навигация), безопаснее очищать кеш в обработчике
`LocationChanged`, если он есть. Если обработчика нет — оставить как есть, пометив комментарием.

**1.2. Отмена в загрузке справочников.** В обоих `foreach`-циклах загрузки справочников
(типы 5 и 9) заменить `catch { … }` на:

```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"[ClayGrid] Справочник колонки '{col.Column}' не загружен: {ex.Message}");
}
```

**1.3. Отмена в `CreateSharedLinkAsync`.** Заменить пустой `catch { }` на:

```csharp
catch (OperationCanceledException) { throw; }
catch
{
    // DbManager уже передал ошибку в ISqlErrorHandler → ClayErrorBar. Circuit не роняем.
}
```

### Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayDataQuery.cs`

**2.1.** Удалить устаревший путь колоночного фильтра целиком:
- свойство `ColumnFilters` (вместе с атрибутом `[Obsolete]`);
- метод `BuildColumnFilterClause` вместе с окружающими `#pragma warning disable/restore CS0618`.

**2.2.** Проверить поиском по решению, что на `ColumnFilters` и `BuildColumnFilterClause` нет
живых ссылок (кроме XML-doc `CombineWhere`, где `b` описан «например, из BuildColumnFilterClause» —
поправить текст doc на «например, из ClayCompositeSqlBuilder.Build»). Если найдутся вызовы —
перевести их на `CompositeFilter` + `ClayCompositeSqlBuilder.Build` в рамках этого шага.

## Критерии приёмки

- URL с закодированными символами в значениях параметров даёт одинаковый результат во всех
  резолверах и `ApplyUrlParams` (один разбор — один результат).
- Навигация со страницы во время загрузки справочников не пишет в лог ложное «справочник не
  загружен» и не глотает отмену.
- Решение собирается без `CS0618` и без директив `#pragma warning disable CS0618`
  (грепнуть — их больше нет в гриде).
- Все тесты грида и фильтра проходят.
