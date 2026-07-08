# ConfirmDialog

Диалог подтверждения действия с иконкой предупреждения.

## Параметры

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Title` | `string` | — | Заголовок |
| `Message` | `string` | — | Текст сообщения |
| `ConfirmText` | `string` | `"Да"` | Текст кнопки подтверждения |
| `CancelText` | `string` | `"Отмена"` | Текст кнопки отмены |

## Возврат

- Подтверждение: `DialogResult.Ok(true)`
- Отмена: `Cancel()`

## Пример

```csharp
var parameters = new DialogParameters<ConfirmDialog>
{
    { x => x.Message, "Удалить запись?" }
};
var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
var dialog = await DialogService.ShowExAsync<ConfirmDialog>("Подтверждение", parameters, options);
var result = await dialog.Result;

if (result is not null && !result.Canceled)
{
    await entity.DeleteAsync(Db);
}
```
