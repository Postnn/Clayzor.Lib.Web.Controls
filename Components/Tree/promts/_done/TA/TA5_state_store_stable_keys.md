# TA5 — ClaySqlTreeStateStore: нестабильные ключи состояния (`string.GetHashCode`)

## Контекст

Файл `Clayzor.Lib.Web.Controls/Components/Tree/State/ClaySqlTreeStateStore.cs`, метод
`BuildParamNames`. Три дефекта в одной строке `Math.Abs(treeId.GetHashCode()).ToString("X6")`:

1. **`string.GetHashCode()` в .NET (Core) рандомизирован на каждый запуск процесса.**
   Имя параметра, под которым состояние сохранено сегодня, после рестарта приложения станет
   другим → сохранённое состояние никогда не читается, а таблица `vwНастройки` засоряется
   «осиротевшими» записями с каждым рестартом. Это полная неработоспособность персистенции.
2. **`Math.Abs(int.MinValue)`** бросает `OverflowException` — редкий, но реальный краш.
3. **`ToString("X6")`** — формат задаёт МИНИМУМ 6 символов, реальная длина до 8. При длинном
   `StateParamPrefix` строки `anchor` и `sel` обрезаются до 20 символов и могут совпасть
   (суффикс `_s` отрезается) → якорь и выделение пишутся в один ключ, затирая друг друга.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/State/ClaySqlTreeStateStore.cs`

**1.1.** Добавить в класс приватный статический метод стабильного хеша (FNV-1a, 32 бита —
детерминирован между запусками и платформами):

```csharp
/// <summary>
/// Стабильный (между запусками процесса) 32-битный хеш FNV-1a.
/// string.GetHashCode() использовать нельзя: в .NET он рандомизируется на каждый старт,
/// а имя параметра — персистентный ключ в БД.
/// </summary>
private static uint StableHash(string s)
{
    unchecked
    {
        uint hash = 2166136261;
        foreach (var ch in s)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash;
    }
}
```

**1.2.** Переписать `BuildParamNames`:

```csharp
/// <summary>
/// Строит имена параметров: {prefix}{hash8} и {prefix}{hash8}_s. Гарантии:
/// длина ≤ 20 (varchar(20)), имена всегда различны — при нехватке места
/// усечению подлежит ПРЕФИКС, суффиксы хеша и «_s» сохраняются.
/// </summary>
private (string anchor, string sel) BuildParamNames(string treeId)
{
    var hash = StableHash(treeId).ToString("X8"); // ровно 8 символов
    var prefix = _treeSettings.Value.StateParamPrefix;

    // anchor: prefix + 8; sel: prefix + 8 + "_s" (10). Урезаем префикс под лимит 20.
    const int maxLen = 20;
    var selTail = hash + "_s";                       // 10 символов
    var maxPrefixLen = maxLen - selTail.Length;      // 10
    if (prefix.Length > maxPrefixLen)
        prefix = prefix[..maxPrefixLen];

    return (prefix + hash, prefix + selTail);
}
```

**1.3. Миграция (обязательный комментарий в код и в AGENTS.md дерева):** старые записи,
сохранённые под рандомизированным хешем, прочитаны быть не могут ни при каком коде — они и так
были нечитаемы после рестарта. Ничего конвертировать не нужно; при желании администратора
осиротевшие записи чистятся по префиксу `StateParamPrefix` в таблице пользовательских параметров.

## Критерии приёмки

- Юнит-тесты:
  - `BuildParamNames("Tree1")` возвращает одинаковую пару при двух вызовах (тривиально) и —
    главное — значение захардкожено в тесте (снапшот), чтобы падать при любой смене алгоритма;
  - `anchor != sel` при `StateParamPrefix` длиной 0, 5, 10, 15, 30 символов;
  - обе строки ≤ 20 символов при тех же префиксах;
  - `StableHash("")`, `StableHash("Tree1")` — фиксированные ожидаемые значения
    (посчитать один раз и захардкодить).
- Ручная проверка: раскрыть узел, перезапустить приложение, открыть страницу — путь к якорю
  восстановился.
