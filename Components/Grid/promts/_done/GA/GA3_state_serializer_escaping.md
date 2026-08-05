# GA3 — GridStateSerializer: разделители ломаются на именах колонок с `,` и `:`

## Контекст

Файл `Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/GridStateSerializer.cs`. Формат колонок —
`"sql1:0,sql2:1"`, сортировки — `"col1:asc,col2:desc"`, групп — `"col1,col2"`. Разделители `,`
и `:` не экранируются, а SqlName в этом решении — **русское имя из БД**, которое вполне может
содержать запятую или двоеточие (вычисляемые выражения-колонки, псевдонимы вида
`CONVERT(...):код`, имена с запятой в скобках).

Последствия:
- `SerializeColumns` для колонки `"Дата, время"` даст `Дата, время:1`, а `DeserializeColumns`
  разобьёт по `,` на `"Дата"` и `" время:1"` → первая часть отбрасывается (нет `:`), состояние
  колонки теряется молча (`.Where(p => p.Length == 2)`).
- То же для сортировки/групп.

Это не только косметика: потеря состояния колонок ведёт к тому, что при каждом сохранении
набор «съеживается», а `ApplyColumnsState` затем добавляет «пропавшие» колонки в конец с
дефолтной видимостью — порядок и видимость пользователя тихо сбрасываются.

Исправление: перейти на устойчивый формат. Минимально-инвазивный вариант — процентное
экранирование разделителей в значениях (`,`→`%2C`, `:`→`%3A`, `%`→`%25`). Формат остаётся
совместимым по структуре; старые значения без спецсимволов читаются как раньше.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Grid/Dynamic/GridStateSerializer.cs`

**1.1.** Добавить в класс два приватных статических помощника:

```csharp
/// <summary>Экранирует разделители формата в значении токена: % , : → %25 %2C %3A.</summary>
private static string Esc(string s)
    => s.Replace("%", "%25").Replace(",", "%2C").Replace(":", "%3A");

/// <summary>Обратное преобразование к <see cref="Esc"/>.</summary>
private static string Unesc(string s)
    => s.Replace("%3A", ":").Replace("%2C", ",").Replace("%25", "%");
```

Порядок замен в `Unesc` важен: `%25` последним, иначе `%252C` разъедется неверно.

**1.2. `SerializeColumns`** — обернуть имя: заменить
`.Select(name => $"{name}:{(hidden.Contains(name!) ? 0 : 1)}")`
на
`.Select(name => $"{Esc(name!)}:{(hidden.Contains(name!) ? 0 : 1)}")`.

**1.3. `DeserializeColumns`** — снимать экранирование и разбивать `:` только по последнему
вхождению (значение `0/1` не содержит спецсимволов, но имя после Unesc может содержать `:`):

```csharp
return value.Split(',')
    .Select(part =>
    {
        var idx = part.LastIndexOf(':');
        if (idx <= 0) return ((string?)null, 0);
        var name = Unesc(part[..idx]);
        return int.TryParse(part[(idx + 1)..], out var vis) ? (name, vis) : (null, 0);
    })
    .Where(t => t.Item1 is not null)
    .Select(t => (SqlName: t.Item1!, Visible: t.Item2))
    .ToList();
```

**1.4. `SerializeSort`** — `.Select(s => $"{Esc(s.Column)}:{(s.Desc ? "desc" : "asc")}")`.

**1.5. `DeserializeSort`** — по аналогии с колонками, разбивать по последнему `:`, применять
`Unesc` к имени:

```csharp
return value.Split(',')
    .Select(part =>
    {
        var idx = part.LastIndexOf(':');
        if (idx <= 0) return (null, false);
        return ((string?)Unesc(part[..idx]), part[(idx + 1)..] == "desc");
    })
    .Where(t => t.Item1 is not null)
    .Select(t => new SortColumn(t.Item1!, t.Item2))
    .ToList();
```

**1.6. `SerializeGroups`** — `string.Join(",", groupColumns.Select(Esc))`.

**1.7. `DeserializeGroups`** —
`value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Unesc).ToList()`.

### Шаг 2 — миграция (комментарий, без кода)

Старые сохранённые значения без спецсимволов читаются новым кодом без изменений (Unesc no-op).
Значения, ранее «съевшие» имя с запятой, и так были испорчены — их потеря не регресс.
Отдельная миграция БД не требуется.

## Критерии приёмки

- Round-trip тесты для имён с `,`, `:`, `%`:
  `SerializeColumns` → `DeserializeColumns` возвращает исходные SqlName и видимость;
  аналогично для sort и groups.
- Обратная совместимость: строка `"a:1,b:0"` (без спецсимволов) читается как и прежде.
- Существующие тесты сериализации (`TG6_state_serialization_tests`) проходят; при необходимости
  добавить новые кейсы со спецсимволами.
