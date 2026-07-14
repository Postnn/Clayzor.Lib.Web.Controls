> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G8 — разбор URL-фильтра `КлючURL=op~value` + правила 1,2,5

Прочитать перед началом: `ClayFilterUrlHelper`, `ColumnFilter`, `ColumnFilterOperator`,
`ClayFilterGroupNode`, `ClayCompositeSqlBuilder` — как условие фильтра представлено и как
попадает в SQL.

Файл создать (Components/Grid/Dynamic/): `ClayGridUrlFilterParser` — ЧИСТАЯ логика (без БД/URL API):
```csharp
public sealed record ParsedUrlFilter(
    string UrlKey, ColumnFilterOperator Operator, string Value,
    bool IsDefault,   // параметр пришёл с префиксом '_'  (правило 1)
    bool IsForced);   // параметр пришёл без '_'          (правило 2)
```
Метод `static ParsedUrlFilter Parse(string rawParamName, string rawValue, ClayColumnDefinition col)`:
1. `IsDefault = rawParamName.StartsWith("_")`; `IsForced = !IsDefault`; фактический ключ = имя без ведущего '_'.
2. Значение `op~value`: если содержит "~" и левая часть — известный оператор → взять этот
   оператор и правую часть как value; поддержать многочастные (`between~a~b`, `in~a,b,c`).
   Если "op~" НЕТ → оператор = `ClayDefaultOperator.For(col.Type)` (правило 5), value = всё значение.
3. Сопоставление имени параметра с колонкой — по `col.UrlKey`.

Метод слияния в фильтр `static void Apply(ClayFilterGroupNode root, IEnumerable<ParsedUrlFilter> parsed, IReadOnlyDictionary<string,string> savedUserParams, ...)`:
- правило 1 (`IsDefault`): применять значение ТОЛЬКО если у пользователя нет сохранённого
  значения соответствующего параметра;
- правило 2 (`IsForced`): применять всегда; такие условия помечать «не сохранять» (см. G9);
- собранные условия добавлять в тот же композитный фильтр (`ColumnFilter` в `root.Nodes`).

Не делай: не трогай существующий разбор UI-фильтра — только добавляешь URL-слой поверх;
неизвестный оператор в "op~" → трактуй строку как обычное value с дефолтным оператором (не падать).

Проверка (TG4, [Theory]):
- "eq~DQA1" → (Equals,"DQA1"); "ge~20260101" → (GreaterOrEqual,"20260101"); "in~3,5" → (In,"3,5");
  "between~20260101~20260401" → (Between, обе границы); "20260101" (для Date) → (дефолт Date, "20260101");
- `_created` без сохранённого → применён; `_created` с сохранённым → победил сохранённый;
- `created` (без '_') → применён и IsForced==true.
