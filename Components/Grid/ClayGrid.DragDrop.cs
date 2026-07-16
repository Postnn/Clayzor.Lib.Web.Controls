using Microsoft.AspNetCore.Components.Web;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>Индекс перетаскиваемого чипа в трее группировки (-1 = нет перетаскивания).</summary>
    private int _dragSourceIndex = -1;

    /// <summary>Флаг раскрытия панели группировки.</summary>
    private bool _trayExpanded = false;

    /// <summary>
    /// Включает/выключает панель группировки.
    /// При выключении очищает группировку и перезагружает данные в плоском режиме.
    /// </summary>
    private async Task ToggleTray()
    {
        _trayExpanded = !_trayExpanded;
        TrayStateChanged?.Invoke();
        if (!_trayExpanded)
        {
            _groupColumns.Clear();
            if (Dynamic) ResetDynamicExpandedGroups();
            _pageNumber = 1;
            await NotifyQueryChanged();
        }
        else
        {
            StateHasChanged();
        }
    }

    private void OnChipDragStart(DragEventArgs e, int index)
    {
        e.DataTransfer.EffectAllowed = "move";
        _dragSourceIndex = index;
        ClayDragState.DraggedColumn = _groupColumns[index];
    }

    private void OnChipDragEnd()
    {
        _dragSourceIndex = -1;
        ClayDragState.DraggedColumn = null;
    }

    private void OnTrayDragOver(DragEventArgs e)
    {
        e.DataTransfer.DropEffect = "move";
    }

    private async Task OnTrayDrop(DragEventArgs e, int targetIndex)
    {
        var draggedData = ClayDragState.DraggedColumn;
        ClayDragState.DraggedColumn = null;

        if (!string.IsNullOrEmpty(draggedData)
            && _columnBySqlName.TryGetValue(draggedData, out var m) && m.Groupable
            && !_groupColumns.Contains(draggedData))
        {
            _groupColumns.Add(draggedData);
            if (Dynamic) ResetDynamicExpandedGroups();
            _dragSourceIndex = -1;
            _pageNumber = 1;
            StateHasChanged();
            await NotifyQueryChanged();
            return;
        }

        if (_dragSourceIndex < 0 || _dragSourceIndex >= _groupColumns.Count)
            return;

        var item = _groupColumns[_dragSourceIndex];
        _groupColumns.RemoveAt(_dragSourceIndex);

        if (targetIndex < 0 || targetIndex >= _groupColumns.Count + 1)
            _groupColumns.Add(item);
        else if (targetIndex > _dragSourceIndex)
            _groupColumns.Insert(targetIndex - 1, item);
        else
            _groupColumns.Insert(targetIndex, item);

        _dragSourceIndex = -1;
        if (Dynamic) ResetDynamicExpandedGroups();
        _pageNumber = 1;
        StateHasChanged();
        await NotifyQueryChanged();
    }
}
