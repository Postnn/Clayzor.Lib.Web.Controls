# GA2 — Коллизии имён параметров в детальных запросах групп (`fullKey.GetHashCode()`)

## Контекст

Файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.Grouping.cs`, метод построения ID
потомков групп (ленивая догрузка для «выбрать все в группе»). Префикс имён Dapper-параметров:

```csharp
var prefix = $"gk_{fullKey.GetHashCode() & 0x7FFFFFFF}_";
```

Два дефекта:

1. **Коллизии.** `string.GetHashCode()` не инъективен: две разные `fullKey` могут дать один хеш.
   Цикл идёт `foreach (var fullKey in groupFullKeys)` — но каждый `fullKey` обрабатывается своим
   `DynamicParameters` (новый `dp` внутри цикла), поэтому коллизия между итерациями не фатальна.
   Однако если этот код когда-либо соберёт параметры нескольких групп в один `dp` (а рефактор
   «выбрать все на странице» ровно к этому и ведёт — см. `GB9_select_all_dynamic_rows`),
   одинаковый префикс даст `System.ArgumentException: parameter '@gk…0' already added` или тихую
   подмену значений → выбор ID не той группы.
2. **Нестабильность.** Хеш зависит от запуска процесса — имена параметров разные при каждом
   старте. Само по себе не баг (имена живут в пределах запроса), но затрудняет отладку и
   снапшот-тесты SQL.

Исправление: заменить хеш на детерминированный порядковый индекс группы в текущем наборе.
Индекс уникален по построению и стабилен.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.Grouping.cs`

Заменить цикл `foreach (var fullKey in groupFullKeys)` на индексируемый и построить префикс из
индекса. Конкретно:

```csharp
var groupList = groupFullKeys.ToList();
for (var gi = 0; gi < groupList.Count; gi++)
{
    var fullKey = groupList[gi];
    var keys = fullKey.Split('\u001F');
    var rawKeys = keys.Select(k => k.Length == 0 ? null : (object?)k).ToList();

    var prefix   = $"gk{gi}_";   // индекс группы уникален и детерминирован
    var keyWhere = ClayGroupingEngine.BuildGroupKeyWhere(
        _dynamicGroupExprs, rawKeys, prefix, out var keyParams);

    if (keyWhere.Length == 0) continue;
    // ... остальное тело без изменений ...
}
```

(Проверить точное имя переменной с набором ключей — в текущем коде `groupFullKeys`; если это
`IEnumerable`, `ToList()` обязателен для индексации.)

Убедиться, что `BuildGroupKeyWhere` формирует имена как `{prefix}{i}` (уже так) — тогда полное имя
параметра будет `gk{gi}_{i}`, уникальное по паре (группа, уровень).

## Критерии приёмки

- Снапшот-тест: для набора из 3 групп имена параметров детальных запросов —
  `gk0_0`, `gk1_0`, `gk2_0` (и т.д. по уровням), без хешей.
- Стресс-тест: две группы, чьи `fullKey` раньше давали коллизию хеша (подобрать или сэмулировать),
  теперь дают разные префиксы; сбор их параметров в один `DynamicParameters` не бросает
  `parameter already added`.
- Существующие тесты `BuildGroupKeyWhere` (`GN3_group_key_where`) проходят без изменений
  (сигнатура метода не менялась).
