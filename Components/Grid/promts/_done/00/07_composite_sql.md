# 07. Генерация SQL по дереву фильтра

Построить `WHERE` из дерева `ClayFilterGroupNode` (задача 02) рекурсивно,
переиспользуя логику одного условия. Безопасность — по правилам существующего кода
плюс закрытие брешей, возникающих из-за дерева.

## Подготовка существующего кода (`ClayDataQuery.cs`)
- `BuildSingleClause`: `private static` → `internal static` (его вызовет новый билдер).
  После задачи 03 значение прогоняется через `descriptor.ToParameter(value)`.

## Новый файл `Components/Grid/Filter/ClayCompositeSqlBuilder.cs`
```csharp
public static class ClayCompositeSqlBuilder
{
    /// Фрагмент WHERE (без слова WHERE) из дерева; null — если активных условий нет.
    public static string? Build(
        ClayFilterGroupNode? root,
        DynamicParameters parameters,
        ISet<string> knownColumns,                                  // белый список SqlName из реестра
        IReadOnlyDictionary<string, string>? columnNameMap = null)  // как в BuildColumnFilterClause
    {
        if (root is null) return null;
        var counter = 0;
        return BuildGroup(root, parameters, knownColumns, columnNameMap, ref counter);
    }
    // BuildGroup: дочерние фрагменты → соединить через AND/OR → обернуть в ().
    // Лист (ColumnFilter): cf.Column ∈ knownColumns? иначе ПРОПУСТИТЬ; уникальные имена параметров;
    //   вызвать ClayDataQuery.BuildSingleClause.
}
```

## Требования безопасности
1. **Имя колонки только из белого списка.** Лист с `cf.Column ∉ knownColumns`
   отбрасывается. Имя интерполируется в SQL (как в `BuildSingleClause`) → источник
   только реестр колонок, никогда пользовательский ввод.
2. **Значения — только Dapper-параметрами.** Без конкатенации.
3. **Уникальные имена параметров.** В дереве колонка повторяется → схема
   `cf_<col>`/`cf2_<col>` даст коллизии. Билдер переназначает имена сквозным
   счётчиком (`p0, p1, …`), игнорируя сохранённые `ParamName`.
4. **Инвариантная культура** для дат/чисел (берётся из дескриптора, задача 03).

## Критерии
- [ ] Группа И/ИЛИ → `AND`/`OR` со скобками; вложенность сохраняется.
- [ ] Лист с неизвестной колонкой отбрасывается (тест на инъекцию через `Column`).
- [ ] Значения параметризуются; SQL в значении не меняет текст запроса.
- [ ] Имена параметров уникальны при повторе колонки.
- [ ] `dotnet build` без ошибок.
