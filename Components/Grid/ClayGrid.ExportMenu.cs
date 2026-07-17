using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>Флаг выполнения операции экспорта/печати (показывает оверлей).</summary>
    private bool _isExporting;

    /// <summary>Подпись текущей долгой операции. null — операция не идёт.</summary>
    private string? _busyLabel;

    /// <summary>Состояние раскрытия подгрупп меню групповых операций: label → isOpen.</summary>
    private Dictionary<string, bool> _openSubGroups = [];

    /// <summary>
    /// Выполняет долгую операцию (печать/экспорт) с видимой блокирующей индикацией.
    /// StateHasChanged ставит рендер в очередь, но батч уедет клиенту только когда метод
    /// уступит поток — поэтому Task.Yield() перед работой обязателен: генерация книги
    /// синхронна и до первого await может пройти секунды.
    /// </summary>
    /// <param name="label">Подпись под индикатором, напр. «Выгрузка в Excel…».</param>
    /// <param name="work">Тело операции.</param>
    private async Task RunBusyAsync(string label, Func<Task> work)
    {
        _busyLabel   = label;
        _isExporting = true;
        StateHasChanged();
        await Task.Yield();
        try
        {
            await work();
        }
        finally
        {
            _isExporting = false;
            _busyLabel   = null;
            StateHasChanged();
        }
    }

    private void ToggleSubGroup(string label)
    {
        if (_openSubGroups.TryGetValue(label, out var isOpen))
            _openSubGroups[label] = !isOpen;
        else
            _openSubGroups[label] = true;
    }

    private bool IsSubGroupOpen(string label)
        => _openSubGroups.TryGetValue(label, out var isOpen) && isOpen;

    // ── Разрешение колонок для печати/экспорта ───────────────────────────────────

    /// <summary>
    /// Запрашивает у пользователя состав колонок для печати/экспорта.
    /// Три исхода: «настроить» → диалог колонок (без сортировки), «как на странице» →
    /// текущие видимые колонки, «отмена» → null.
    /// </summary>
    /// <param name="contextLabel">Контекст операции, напр. «печати (все данные)».</param>
    /// <returns>Список колонок или null, если операция отменена.</returns>
    private async Task<IReadOnlyList<ClayColumnMeta>?> ResolveExportColumnsAsync(string contextLabel)
    {
        // 1. Спросить пользователя
        var promptParams = new DialogParameters<ClayColumnSettingsPromptDialog>
        {
            { x => x.ContextLabel, contextLabel }
        };
        var promptOptions = new DialogOptionsEx
        {
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true,
            DragMode = MudDialogDragMode.Simple,
        };
        var promptDialog = await DialogService.ShowExAsync<ClayColumnSettingsPromptDialog>(
            "Выбор колонок", promptParams, promptOptions);
        var promptResult = await promptDialog.Result;

        if (promptResult is null || promptResult.Canceled)
            return null;

        // 2. «Как на странице»
        if (promptResult.Data is false)
            return ((IClayGrid)this).GetVisibleColumns();

        // 3. «Настроить» — диалог колонок без сортировки
        var items = BuildColumnSettingsItems();
        // Сбрасываем SortPriority/IsSortDesc — в режиме печати/экспорта сортировка не нужна
        foreach (var item in items)
        {
            item.SortPriority = 0;
            item.IsSortDesc = false;
        }

        var settingsParams = new DialogParameters<ClayColumnSettingsDialog>
        {
            { x => x.Items, items },
            { x => x.ShowSorting, false },
        };
        var settingsOptions = new DialogOptionsEx
        {
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true,
            DragMode = MudDialogDragMode.Simple,
        };
        // Заголовок явно называет контекст
        var title = contextLabel.StartsWith("печати") ? "Колонки для печати" : "Колонки для выгрузки в Excel";
        var settingsDialog = await DialogService.ShowExAsync<ClayColumnSettingsDialog>(
            title, settingsParams, settingsOptions);
        var settingsResult = await settingsDialog.Result;

        if (settingsResult is null || settingsResult.Canceled || settingsResult.Data is not List<ColumnSettingsItem> updatedItems)
            return null;

        // Построить результат в порядке, заданном пользователем
        var resolved = new List<ClayColumnMeta>();
        foreach (var item in updatedItems.Where(i => i.IsVisible))
        {
            var meta = ((IClayGrid)this).GetColumnMeta(item.SqlName);
            if (meta is not null)
                resolved.Add(meta);
        }

        return resolved;
    }

    // ── Печать ───────────────────────────────────────────────────────────────────

    private async Task PrintCurrentPageInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        var columns = await ResolveExportColumnsAsync("печати (текущая страница)");
        if (columns is null) return;

        string? html = null;
        await RunBusyAsync("Подготовка печатной формы…", async () =>
        {
            html = Dynamic
                ? await BuildDynamicPrintHtmlForCurrentPage(
                      columns, BuildFilterDescription(), BuildGroupDescription())
                : await DataLoader!.BuildPrintHtmlForCurrentPageAsync(
                      columns, Title, BuildFilterDescription(), BuildGroupDescription());
        });

        if (html is null) return;
        try
        {
            await JS.InvokeAsync<object>("clayGridPrint.printHtml", html);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка печати: {ex.Message}", Severity.Error);
        }
    }

    private async Task PrintAllInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        var columns = await ResolveExportColumnsAsync("печати (все данные)");
        if (columns is null) return;

        string? html = null;
        await RunBusyAsync("Подготовка печатной формы…", async () =>
        {
            html = Dynamic
                ? await BuildDynamicPrintHtmlForAll(
                      columns, BuildFilterDescription(), BuildGroupDescription())
                : await DataLoader!.BuildPrintHtmlAsync(
                      columns, Title, BuildFilterDescription(), BuildGroupDescription());
        });

        if (html is null) return;
        try
        {
            await JS.InvokeAsync<object>("clayGridPrint.printHtml", html);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка печати: {ex.Message}", Severity.Error);
        }
    }

    private async Task PrintSelectedInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        if (_selectedIds.Count == 0) return;
        var columns = await ResolveExportColumnsAsync("печати (выбранные записи)");
        if (columns is null) return;

        string? html = null;
        await RunBusyAsync("Подготовка печатной формы…", async () =>
        {
            html = Dynamic
                ? await BuildDynamicPrintHtmlForSelected(
                      columns, _selectedIds.ToList(), BuildFilterDescription(), BuildGroupDescription())
                : await DataLoader!.BuildPrintHtmlForSelectedAsync(
                      columns, Title, _selectedIds.ToList(),
                      BuildFilterDescription(), BuildGroupDescription());
        });

        if (html is null) return;
        try
        {
            await JS.InvokeAsync<object>("clayGridPrint.printHtml", html);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка печати: {ex.Message}", Severity.Error);
        }
    }

    // ── Excel ────────────────────────────────────────────────────────────────────

    private async Task ExcelCurrentPageInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        var columns = await ResolveExportColumnsAsync("выгрузки в Excel (текущая страница)");
        if (columns is null) return;

        await RunBusyAsync("Выгрузка в Excel…", async () =>
        {
            var request = new ExcelExportRequest
            {
                Mode = ExcelExportMode.CurrentPage,
                Title = Title,
                VisibleColumns = columns,
                FilterDescription = BuildFilterDescription(),
                GroupDescription = BuildGroupDescription(),
            };

            if (Dynamic)
                await DynamicExcelExportAsync(request);
            else
                await DataLoader!.ExcelExportAsync(request);
        });
    }

    private async Task ExcelAllInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        var columns = await ResolveExportColumnsAsync("выгрузки в Excel (все данные)");
        if (columns is null) return;

        await RunBusyAsync("Выгрузка в Excel…", async () =>
        {
            var request = new ExcelExportRequest
            {
                Mode = ExcelExportMode.All,
                Title = Title,
                VisibleColumns = columns,
                FilterDescription = BuildFilterDescription(),
                GroupDescription = BuildGroupDescription(),
            };

            if (Dynamic)
                await DynamicExcelExportAsync(request);
            else
                await DataLoader!.ExcelExportAsync(request);
        });
    }

    private async Task ExcelSelectedInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        if (_selectedIds.Count == 0) return;
        var columns = await ResolveExportColumnsAsync("выгрузки в Excel (выбранные записи)");
        if (columns is null) return;

        await RunBusyAsync("Выгрузка в Excel…", async () =>
        {
            var request = new ExcelExportRequest
            {
                Mode = ExcelExportMode.Selected,
                Title = Title,
                VisibleColumns = columns,
                SelectedIds = _selectedIds.ToList(),
                FilterDescription = BuildFilterDescription(),
                GroupDescription = BuildGroupDescription(),
            };

            if (Dynamic)
                await DynamicExcelExportAsync(request);
            else
                await DataLoader!.ExcelExportAsync(request);
        });
    }
}
