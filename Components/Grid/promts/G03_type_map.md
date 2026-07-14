> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G3 — маппинг Тип(int) → дескриптор + дефолтный оператор + разбор Формат (типы 1,2,3,4,7)

Прочитать перед началом: `Components/Grid/ColumnTypes/ColumnTypeDescriptor.cs`,
`ColumnTypeRegistry.cs`, и дескрипторы Number/Decimal/Date/Text/Boolean — какие поля/методы
у дескриптора, как он выбирается сейчас.

Файлы создать (Components/Grid/Dynamic/):
- Enum спецификационных типов:
```csharp
public enum ClayColumnKind {
    Number = 1, Text = 2, Date = 3, Link = 4, List = 5, ConditionBool = 6,
    Bool = 7, Html = 8, Icon = 9, DateTimeLocal = 10, ConditionList = 11,
    LimitedText = 12, TimeLocal = 13
}
```
- `ClayColumnTypeMap`:
  - `static ColumnTypeDescriptor? Resolve(int тип)` — вернуть дескриптор для 1→Number, 2→Text,
    3→Date, 4→(тип «ссылка», вывод как в G13; пока как Text-подобный без сортируемого html),
    7→Boolean. Для 5,6,8,9,10,11,12,13 → вернуть null (unsupported) БЕЗ исключения.
  - Признак поддержки: `static bool IsSupported(int тип)`.
- Дефолтный оператор фильтра для правила 5. Добавь на дескриптор свойство
  `ColumnFilterOperator DefaultFilterOperator { get; }` (или отдельный `ClayDefaultOperator.For(kind)`):
  Text→Contains, Number→Equals, Date→Equals, Bool→Equals. (Точные имена операторов возьми из
  enum `ColumnFilterOperator`.)
- Разбор Формат: `ClayColumnFormat.Parse(int тип, string? формат)` возвращает то, что нужно типу:
  Число → числовой .NET-формат (напр. "N2"); Дата → формат даты (если пусто — вывод «как есть»);
  Булево → sql-условие TRUE (строка "Активно=1", используется в рендере ClayCheckBox из G4/следующих).

Не делай: не реализуй пока типы 5–13 (кроме 4 как простой ссылки) — они в фазах 4–5.

Проверка (TG3):
- `Resolve(1)`→Number, `Resolve(2)`→Text, `Resolve(3)`→Date, `Resolve(7)`→Boolean; `Resolve(5)`→null, `IsSupported(5)`==false;
- `DefaultFilterOperator` для Text==Contains, Number==Equals;
- `ClayColumnFormat.Parse(3,"dd.MM.yyyy")` → формат "dd.MM.yyyy"; `Parse(7,"Активно=1")` → условие "Активно=1".
