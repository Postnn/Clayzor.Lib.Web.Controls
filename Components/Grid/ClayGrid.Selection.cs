using Clayzor.Lib.Entities;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>Флаг режима выбора записей.</summary>
    private bool _selectMode;

    /// <summary>Состояние чекбокса «выбрать всё» в заголовке.</summary>
    private bool _selectAllChecked;

    /// <summary>ID выбранных сущностей (персистентно между страницами).</summary>
    private HashSet<int> _selectedIds = [];

    /// <summary>ID выбранных сущностей (персистентно между страницами).</summary>
    public IReadOnlyCollection<int> SelectedIds => _selectedIds;

    private void ToggleSelectMode()
    {
        _selectMode = !_selectMode;
        if (!_selectMode)
        {
            _selectedIds.Clear();
            _groupChildIds.Clear();
            _selectAllChecked = false;
        }
        _dataKey++;
        StateHasChanged();
    }

    /// <summary>
    /// true — некоторые (но не все) сущности на текущей странице выбраны.
    /// Учитывает как DetailRow, так и дочерние сущности GroupHeaderRow.
    /// </summary>
    private bool IsHeaderIndeterminate()
    {
        if (Items is null) return false;
        bool anySelected = false;
        bool allSelected = true;
        bool anyItem = false;

        foreach (var row in Items)
        {
            if (row is IDetailRow dr && dr.Item is Entity e)
            {
                anyItem = true;
                if (_selectedIds.Contains(e.Id)) anySelected = true;
                else allSelected = false;
            }
            else if (row is GroupHeaderRow gh)
            {
                var childIds = GetChildIdsForGroup(gh.FullKey);
                if (childIds is not null && childIds.Count > 0)
                {
                    anyItem = true;
                    bool groupAllSelected = true;
                    foreach (var id in childIds)
                    {
                        if (_selectedIds.Contains(id)) anySelected = true;
                        else groupAllSelected = false;
                    }
                    if (!groupAllSelected) allSelected = false;
                }
            }
        }

        return anyItem && anySelected && !allSelected;
    }

    /// <summary>
    /// Вычисляет состояние чекбокса в заголовке: все ли сущности на текущей странице выбраны.
    /// </summary>
    private bool ComputeSelectAllState()
    {
        if (Items is null) return false;
        bool anyItem = false;

        foreach (var row in Items)
        {
            if (row is IDetailRow dr && dr.Item is Entity e)
            {
                anyItem = true;
                if (!_selectedIds.Contains(e.Id)) return false;
            }
            else if (row is GroupHeaderRow gh)
            {
                var childIds = GetChildIdsForGroup(gh.FullKey);
                if (childIds is not null && childIds.Count > 0)
                {
                    anyItem = true;
                    foreach (var id in childIds)
                        if (!_selectedIds.Contains(id)) return false;
                }
            }
        }

        return anyItem;
    }

    /// <summary>
    /// Обработчик клика по чекбоксу строки детализации — добавляет/удаляет ID сущности.
    /// </summary>
    private async Task OnRowSelectAsync(int entityId, bool selected)
    {
        if (selected)
            _selectedIds.Add(entityId);
        else
            _selectedIds.Remove(entityId);
        _selectAllChecked = ComputeSelectAllState();
        StateHasChanged();
    }
}
