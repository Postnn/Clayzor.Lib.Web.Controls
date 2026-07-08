using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>
    /// Состояние сортировки: <c>Column</c> содержит <see cref="ClayColumnMeta.SortName"/>
    /// (реальное выражение для ORDER BY), а не SqlName колонки.
    /// </summary>
    private List<SortColumn> _sortState = [];

    /// <summary>
    /// Переключает сортировку по колонке: нет → ASC → DESC → нет.
    /// Принимает SqlName; реальное выражение ORDER BY берётся из
    /// <see cref="ClayColumnMeta.SortName"/> зарегистрированной колонки.
    /// </summary>
    public async Task ToggleSort(string sqlName)
    {
        var sortName = _columnBySqlName.TryGetValue(sqlName, out var meta)
            ? meta.SortName
            : sqlName;

        var idx = _sortState.FindIndex(s => s.Column == sortName);
        if (idx >= 0)
        {
            if (_sortState[idx].Desc)
                _sortState.RemoveAt(idx);
            else
                _sortState[idx] = _sortState[idx] with { Desc = true };
        }
        else
        {
            _sortState.Insert(0, new SortColumn(sortName, false));
            if (_sortState.Count > 2)
                _sortState.RemoveAt(2);
        }

        _pageNumber = 1;
        await NotifyQueryChanged();
        StateHasChanged();
    }

    /// <summary>Синхронная обёртка над <see cref="ToggleSort"/> для onclick-обработчиков.</summary>
    private void HandleSortClick(string sqlName) => _ = ToggleSort(sqlName);

    /// <summary>
    /// Возвращает бейдж сортировки для колонки: номер приоритета + стрелка направления.
    /// Принимает SqlName; поиск в состоянии сортировки ведётся по резолвленному SortName.
    /// </summary>
    public RenderFragment GetSortBadge(string sqlName) => builder =>
    {
        var sortName = _columnBySqlName.TryGetValue(sqlName, out var meta)
            ? meta.SortName
            : sqlName;

        var idx = _sortState.FindIndex(s => s.Column == sortName);
        if (idx < 0) return;
        var arrow = _sortState[idx].Desc ? "\u2193" : "\u2191";
        var label = (idx + 1).ToString() + arrow;
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "chip-sort-badge");
        builder.AddContent(2, label);
        builder.CloseElement();
    };
}
