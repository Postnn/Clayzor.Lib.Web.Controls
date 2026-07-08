# ClayEditForm\<T>

Обёртка `MudDialog` с валидацией, сохранением и удалением.

## Параметры

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Model` | `TEntity` | — | Редактируемая модель |
| `Title` | `string` | — | Заголовок |
| `Icon` | `string` | — | Иконка |
| `ChildContent` | `RenderFragment` | — | Поля формы |
| `OnSave` | `EventCallback<TEntity>` | — | Сохранение |
| `OnDelete` | `EventCallback<TEntity>` | — | Удаление (опционально) |
| `ShowDelete` | `bool` | `true` | Показать кнопку удаления |

## Особенности

- Кнопка **«Сохранить»** — Primary, валидирует форму через `MudForm`, вызывает `OnSave(Model)` и закрывает диалог с `DialogResult.Ok(Model)`
- Кнопка **«Удалить»** — красная outlined, слева (`mr-auto`). Показывается только если `OnDelete.HasDelegate && ShowDelete`

## Пример

```razor
@* MyEntityEditDialog.razor *@
<ClayEditForm TEntity="MyEntity"
               Model="Model"
               Title="Редактирование"
               Icon="@Icons.Material.Filled.Edit"
               OnSave="SaveAsync"
               OnDelete="DeleteAsync">
    <MudTextField @bind-Value="Model.Name" Label="Название" Variant="Variant.Outlined" Required="true" />
    <ClayComboBox TItem="SomeLookup" @bind-Value="Model.LookupId" Items="_lookups" Label="Справочник" />
</ClayEditForm>

@code {
    [Parameter] public MyEntity Model { get; set; } = null!;

    private async Task SaveAsync(MyEntity model)
    {
        if (model.Id == 0)
            await model.InsertAsync(Db);
        else
            await model.UpdateAsync(Db);
    }

    private async Task DeleteAsync(MyEntity model)
    {
        var confirmed = await DialogService.ShowExAsync<ConfirmDialog>(
            "Подтверждение",
            new DialogParameters<ConfirmDialog> { { x => x.Message, "Удалить запись?" } },
            new DialogOptionsEx { DragMode = MudDialogDragMode.Simple });
        var result = await confirmed.Result;
        if (result is not null && !result.Canceled)
            await model.DeleteAsync(Db);
    }
}
```

## Вызов диалога

```csharp
var parameters = new DialogParameters<MyEntityEditDialog> { { x => x.Model, model } };
var options = new DialogOptionsEx { MaxWidth = MaxWidth.Small, FullWidth = true, DragMode = MudDialogDragMode.Simple };
var dialog = await DialogService.ShowExAsync<MyEntityEditDialog>("Заголовок", parameters, options);
var result = await dialog.Result;
if (result is not null && !result.Canceled) { /* успех */ }
```

Требует `@using MudBlazor.Extensions` и `@using MudBlazor.Extensions.Options` в `_Imports.razor`.
