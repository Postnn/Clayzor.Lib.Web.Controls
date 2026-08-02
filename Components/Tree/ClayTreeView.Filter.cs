using Clayzor.Lib.Web.Controls.Components.Filter;
using Clayzor.Lib.Web.Controls.Components.Tree.Helpers;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    // ── Injects ──────────────────────────────────────────────────────────────────

    [Inject] private IDialogService DialogService { get; set; } = default!;

    // ── Fields ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Корень дерева фильтра — единственный источник истины.
    /// Пустая группа (Nodes.Count == 0) означает «фильтр не установлен».
    /// </summary>
    private ClayFilterGroupNode _filterRoot = new();

    /// <summary>Активен ли фильтр (есть хотя бы одно условие).</summary>
    private bool IsFilterActive => _filterRoot.Nodes.Count > 0;

    /// <summary>Количество активных условий — для бейджа на кнопке.</summary>
    private int ActiveFilterCount => ClayFilterDescriptionBuilder.CountActiveLeaves(_filterRoot);

    // ── BuildFilterColumns ───────────────────────────────────────────────────────

    /// <summary>
    /// Строит список фильтруемых полей дерева для диалога настраиваемого фильтра.
    /// Колонки из <see cref="ClayTreeOptions.FilterExcludedColumns"/> исключаются.
    /// Возвращает пустой список, если <see cref="ClayTreeOptions.FilterColumns"/> не задан.
    /// </summary>
    private IReadOnlyList<ClayFilterColumnInfo> BuildFilterColumns()
        => ClayTreeFilterColumnBuilder.Build(Options.FilterColumns, Options.FilterExcludedColumns);

    /// <summary>Ищет DisplayName колонки по SqlName в FilterColumns.</summary>
    private string GetFilterColumnDisplayName(string sqlName)
    {
        if (Options.FilterColumns is { Count: > 0 } cols)
        {
            foreach (var col in cols)
            {
                if (string.Equals(col.SqlName, sqlName, StringComparison.OrdinalIgnoreCase))
                    return col.DisplayName;
            }
        }
        return sqlName;
    }

    // ── Tooltip ──────────────────────────────────────────────────────────────────

    /// <summary>Текстовое представление фильтра для tooltip кнопки.</summary>
    private string? FilterTooltip => ClayFilterDescriptionBuilder.BuildText(
        _filterRoot, GetFilterColumnDisplayName);

    // ── Open dialog ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Открывает диалог настраиваемого фильтра дерева.
    /// Результат заменяет <see cref="_filterRoot"/> и (в TF_E) перезагружает данные по фильтру.
    /// На этом шаге — просто перезагружает дерево в обычном режиме.
    /// </summary>
    private async Task OpenTreeFilterDialogAsync()
    {
        var cols = BuildFilterColumns();
        if (cols.Count == 0)
            return;

        var parameters = new DialogParameters<ClayFilterDialog>
        {
            { x => x.Root,    _filterRoot.Nodes.Count > 0 ? _filterRoot : new ClayFilterGroupNode() },
            { x => x.Columns, (IReadOnlyList<ClayFilterColumnInfo>)cols },
        };
        var options = new DialogOptionsEx
        {
            MaxWidth         = MaxWidth.Medium,
            FullWidth        = false,
            CloseOnEscapeKey = true,
            DragMode         = MudDialogDragMode.Simple,
        };
        var dialog = await DialogService.ShowExAsync<ClayFilterDialog>(
            "Настраиваемый фильтр", parameters, options);
        var result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is ClayFilterGroupNode newRoot)
        {
            // Пустой результат (сброс) — трактовать как «фильтра нет»
            _filterRoot = newRoot.Nodes.Count == 0 ? new ClayFilterGroupNode() : newRoot;
            await ApplyFilterAsync();
        }
    }

    // ── Clear ────────────────────────────────────────────────────────────────────

    /// <summary>Убирает фильтр и перезагружает дерево в обычном режиме.</summary>
    private async Task ClearTreeFilterAsync()
    {
        _filterRoot = new ClayFilterGroupNode();
        await ApplyFilterAsync();
    }

    // ── Apply (заглушка до TF_E) ─────────────────────────────────────────────────

    /// <summary>
    /// Применяет текущий фильтр. На этом шаге — просто перезагружает корни дерева
    /// (без SQL-фильтрации). В TF_E будет заменено на загрузку по фильтру с предками.
    /// </summary>
    private async Task ApplyFilterAsync()
    {
        await ((IClayTreeView)this).ReloadAsync();
        StateHasChanged();
    }
}
