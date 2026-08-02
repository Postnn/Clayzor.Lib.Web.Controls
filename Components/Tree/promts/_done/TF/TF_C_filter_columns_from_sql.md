> Часть серии **TF**. Прочитать `TF0_README_tree_filter.md`, отчёты **TF_A**, **TF_B**.
> Делать ТОЛЬКО этот шаг.

# TF_C — список фильтруемых колонок дерева → `ClayFilterColumnInfo`

Диалог `ClayFilterDialog` принимает `IReadOnlyList<ClayFilterColumnInfo>` (описания полей).
Дерево должно этот список собрать: из колонок своего запроса, исключив заданные в опциях. Пока
только сбор списка — вызов диалога это TF_D.

## Прочитать

- отчёт **TF_A п.5** — принятое решение об источнике колонок и типов (это фундамент шага);
- `Components/Filter/ClayFilterColumnInfo.cs`, `ColumnTypeRegistry`/`ColumnTypeDescriptor`;
- `Components/Tree/ClayTreeOptions.cs` (после TF_B — `FilterExcludedColumns`,
  возможно `FilterColumns`);
- `Clayzor.Lib.Entities/Tree/ClayTreeSchema.cs`.

## Что сделать — по решению TF_A

Ветвление зависит от того, что TF_A определил источником колонок и типов. Реализуй **только
выбранный вариант**:

**Вариант «явный список в опциях»** (если TF_A показал, что типы/имена из запроса не вывести):
- источник — `ClayTreeOptions.FilterColumns` (список `ClayTreeFilterColumn` из TF_B);
- метод (в компоненте или хелпере) строит `IReadOnlyList<ClayFilterColumnInfo>`:
  каждая `ClayTreeFilterColumn` → `ClayFilterColumnInfo` (SqlName, DisplayName,
  `Type = ColumnTypeRegistry.FromKind(...)`, Options), **минус** `FilterExcludedColumns`;
- если `FilterColumns` не задан → фильтр недоступен (кнопки фильтра нет, TF_D учтёт).

**Вариант «из метаданных запроса»** (если TF_A показал, что колонки/типы берутся из результата):
- при первой загрузке из набора колонок результата собрать список; типы — вывести по
  CLR-типу значения через `ColumnTypeRegistry.FromClr` (как грид выводит для статических);
  DisplayName — из `ClayTreeFilterColumn`/схемы, иначе = SqlName;
- исключить `FilterExcludedColumns`.

В обоих вариантах — **один** приватный метод/хелпер, единая точка построения списка:

```csharp
/// <summary>
/// Строит список фильтруемых полей дерева для диалога настраиваемого фильтра.
/// Колонки из FilterExcludedColumns исключаются. Источник типов/имён — см. TF_A.
/// </summary>
private IReadOnlyList<ClayFilterColumnInfo> BuildFilterColumns() { ... }
```

Список фильтруемых колонок дереву нужен и для фильтра, и для разбора query-параметров (TF_G) —
поэтому единый метод, не дублировать.

## Ловушки

- **Исключение колонок — по SqlName, регистронезависимо?** Согласовать с тем, как сравниваются
  имена в остальном дереве (обычно `OrdinalIgnoreCase`). Пустой/несуществующий SqlName в
  `FilterExcludedColumns` — молча игнорировать, не падать.
- **Технические колонки схемы** (`Left`/`Right`/`Level`, а часто и `Id`/`Parent`) фильтровать
  бессмысленно — по умолчанию их в список не включать (или включать? — решить по TF_A и
  отметить). Пользовательские `ExtraColumns` — включать.
- **Дубли** SqlName в списке → диалог покажет колонку дважды. Дедуплицировать по SqlName.

## Не делай

- Не вызывай `ClayFilterDialog` и не строй панель — TF_D.
- Не исполняй фильтр (SQL с предками) — TF_E.
- Не трогай грид и общий фильтр (список строит дерево на своей стороне и передаёт готовый
  `ClayFilterColumnInfo`).
- Не тащи в список гридовые понятия (Groupable, ColumnId) — их у дерева нет.

## Проверка

- unit-тест `BuildFilterColumns` (чистая часть, без БД, если источник — опции):
  - из `FilterColumns` строится список с верными типами;
  - `FilterExcludedColumns` убирает колонку;
  - дедупликация по SqlName;
  - технические колонки исключены (по принятому правилу);
- `dotnet build` + `dotnet test` — зелёные;
- дерево на `/tree-test` работает как раньше (список строится, но пока никем не вызывается —
  можно временно вывести его в отладочную панель страницы для глазной проверки состава, потом
  убрать).
