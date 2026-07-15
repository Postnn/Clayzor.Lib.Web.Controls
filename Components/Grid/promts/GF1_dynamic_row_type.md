> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF1 — тип строки динамического грида: `ClayDynamicRow`

Прочитать перед началом: `Components/Grid/ClayGridRow.cs` (`IClayGridRow`, `IDetailRow`,
`DetailRow<T>`, `GroupHeaderRow`), `Components/Grid/ClayGrid.razor` — секция `<MudDataGrid>`,
разбор `context.Item` в `CellTemplate` сервисной колонки и колонок из `_columnOrder`,
`Components/Grid/ClayGrid.Dynamic.cs` — `InitDynamicMode` (сборка cell-шаблонов) и
`LoadDynamicData`, `Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` — что реально возвращает
`QueryPagedRowsAsync`, `Kesco.App.Web.Inventory/Components/Pages/Home.razor` — чему равен
`TEntity`.

## Дефект

Два дефекта, оба в присвоении `Items`.

**Каст падает.** В `LoadDynamicData`:

```csharp
Items = (IEnumerable<TEntity>)rows;
```

`TEntity` = `IClayGridRow` (см. `Home.razor`), а `QueryPagedRowsAsync` возвращает
`IReadOnlyList<IReadOnlyDictionary<string, object?>>` — фактически
`List<ReadOnlyDictionary<string, object?>>`. `ReadOnlyDictionary` не реализует `IClayGridRow`,
ковариантного преобразования к `IEnumerable<IClayGridRow>` нет → `InvalidCastException`
в рантайме. Компилятор молчит: `TEntity` — параметр типа, каст откладывается на рантайм.

Обрати внимание на порядок строк в методе: `TotalCount` присваивается ДО `Items`. Поэтому
счётчик записей и число страниц выглядят корректными, а строк нет — это ровно тот симптом,
который видит тестировщик.

**Модель строки не сходится с разметкой.** `ClayGrid.razor` разбирает `context.Item` так:

- `is GroupHeaderRow gh` → заголовок группы;
- `is IDetailRow detail` → иначе карандаш и корзина не рисуются вообще;

а cell-шаблоны, собранные в `InitDynamicMode`, ждут:

- `ctx.Item is IReadOnlyDictionary<string, object?> dict` → иначе ячейка пустая.

Оба условия одновременно не выполняет ни голый словарь, ни `DetailRow<T>` — он ограничен
`where T : Entity`, а динамическая строка не `Entity`. Нужен один тип строки, реализующий
и `IDetailRow`, и словарь.

## Изменить/создать

**1.** Создать `Components/Grid/Dynamic/ClayDynamicRow.cs`:

```csharp
using System.Collections;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Строка динамического грида. Одновременно:
/// — <see cref="IDetailRow"/> для сервисной колонки (карандаш/корзина) и экспорта;
/// — словарь колонка→значение для cell-шаблонов, собранных в <c>InitDynamicMode</c>.
/// </summary>
public sealed class ClayDynamicRow : IClayGridRow, IDetailRow, IReadOnlyDictionary<string, object?>
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    public ClayDynamicRow(IReadOnlyDictionary<string, object?> values) => _values = values;

    /// <summary>Сама строка и есть сущность — cell-шаблоны разбирают её как словарь.</summary>
    object? IDetailRow.Item => this;

    public object? this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<object?> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

`IDetailRow.Item => this` — не описка. `GetRowIdValue(detail.Item)` в `ClayGrid.Dynamic.cs`
делает `rowItem is IReadOnlyDictionary<string, object?> dict` и берёт `_dynamicDef.IdColumn`;
раз строка сама словарь, `Item` должен вернуть её же.

**2.** В `ClayGrid.Dynamic.cs`, `LoadDynamicData` — заменить каст на обёртывание:

```csharp
// было: Items = (IEnumerable<TEntity>)rows;
Items = rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)).ToList();
```

Двойной каст `(TEntity)(object)` нужен, потому что компилятор не знает, что `TEntity`
совместим с `ClayDynamicRow`. Ограничение `where TEntity : class` это допускает.

## Не делай

Не трогай статический режим — `DetailRow<T>` и `GroupHeaderRow` остаются как есть, `ClayDynamicRow`
существует рядом. Не вводи вторую модель строки помимо `ClayDynamicRow`. Не меняй сигнатуры
`DynamicSql` — оборачивание делает грид, а не слой `Entities`. Первую загрузку данных НЕ добавляй,
это GF2.

## Проверка

- `dotnet build` зелёный.
- Юнит-тест (в проект тестов Controls): `new ClayDynamicRow(new Dictionary<string, object?> { ["Кол"] = 42 })`
  — проверить, что экземпляр проходит `is IClayGridRow`, `is IDetailRow`,
  `is IReadOnlyDictionary<string, object?>`; что `((IDetailRow)row).Item` ссылочно равен самому
  `row`; что `row["Кол"]` возвращает `42`; что `TryGetValue("Нет", out _)` возвращает `false`.
- Ручная: `?id=140` → грид по-прежнему пуст (первой загрузки ещё нет — это GF2), но в логе/консоли
  БОЛЬШЕ НЕТ `InvalidCastException`. Нажать «Обновить» (иконка в пагинаторе) → строки появились,
  ячейки заполнены, у строк видны карандаш и корзина.
