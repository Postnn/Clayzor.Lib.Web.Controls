# ClayComboBox\<TItem>

Выпадающий список для справочных сущностей, реализующих `ILookupEntity`.
Автоматически использует `Id` как значение, `Name` как отображение.

Рендерит `MudSelect` с фиксированными параметрами:
- `Variant="Variant.Outlined"` — единый стиль полей ввода
- `Margin="Margin.Dense"` — компактная высота (как поля фильтра)
- `Dense="true"` — уменьшенные внутренние отступы
- `PopoverClass="clay-combo-popover"` — CSS-правила в `app.css` (overflow, max-height, line-height, font-size)

## Параметры

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Value` / `ValueChanged` | `int` | — | Выбранный Id (двусторонняя привязка) |
| `Items` | `IEnumerable<TItem>` | — | Элементы справочника |
| `Label` | `string` | — | Подпись |
| `Variant` | `Variant` | `Outlined` | Стиль |
| `Required` | `bool` | `false` | Обязательное поле |
| `RequiredError` | `string` | — | Текст ошибки |
| `Class` | `string` | — | CSS-класс |

## Ограничение

`TItem` обязан реализовывать `ILookupEntity`:
```csharp
public interface ILookupEntity
{
    int Id { get; }
    string Name { get; }
}
```

## Пример

```razor
<ClayComboBox TItem="MedicalTestType"
               @bind-Value="Model.TestTypeId"
               Items="_testTypes"
               Label="Тип исследования"
               Required="true"
               Class="mb-2" />
```

## Реализация справочной сущности

```csharp
public class MedicalTestType : ILookupEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public static async Task<List<MedicalTestType>> GetAllAsync(DbManager db)
    {
        var result = await db.QueryAsync<MedicalTestType>(SQLQueries.SELECT_МедицинскиеАнализыТипы);
        return result.ToList();
    }
}
```
