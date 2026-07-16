using Clayzor.Lib.Entities;
using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>
    /// Событие переключения раскрытия/сворачивания группы.
    /// Страница-потребитель подписывается через <c>OnGroupToggle="ToggleGroup"</c> на теге &lt;ClayGrid&gt;.
    /// Больше не нужно вручную встраивать &lt;ClayGroupHeader&gt; в CellTemplate конкретной колонки.
    /// </summary>
    [Parameter] public EventCallback<GroupHeaderRow> OnGroupToggle { get; set; }

    /// <summary>
    /// Единый обработчик клика по шеврону заголовка группы.
    /// Динамический режим обрабатывает сам; статический — отдаёт странице через OnGroupToggle.
    /// </summary>
    private async Task HandleGroupToggle(GroupHeaderRow header)
    {
        if (Dynamic)
            await ToggleDynamicGroup(header);
        else
            await OnGroupToggle.InvokeAsync(header);
    }

    /// <summary>
    /// SqlName колонки, которая должна отображать заголовок группы (шеврон + подпись + счётчик).
    /// "__edit__" — сервисная колонка. Условие должно совпадать с условием рендеринга
    /// сервисной колонки в ClayGrid.razor (EditDialogType is not null || HasDynamicEdit),
    /// иначе заголовок группы уедет в другую колонку, а CellClassFunc сервисной колонки
    /// не проставит group-header-cell.
    /// Никогда не совпадает с колонкой, скрытой текущей группировкой или пользовательскими
    /// настройками — вычисляется заново на каждый рендер.
    /// </summary>
    private string GroupRowHostKey
    {
        get
        {
            if (EditDialogType is not null || HasDynamicEdit) return "__edit__";
            foreach (var colId in _columnOrder)
            {
                if (!_columnById.TryGetValue(colId, out var meta)) continue;
                if (_hiddenSqlNames.Contains(meta.SqlName)) continue;
                if (IsGrouped(meta.SqlName)) continue;
                return meta.SqlName;
            }
            return "";
        }
    }

    private bool IsGroupRowHost(string sqlName) => GroupRowHostKey == sqlName;

    /// <summary>SQL-имена колонок текущей группировки.</summary>
    private List<string> _groupColumns = [];

    /// <summary>
    /// Кеш: FullKey группы → ID дочерних сущностей.
    /// Заполняется лениво при первом взаимодействии с чекбоксом группы в grouped-режиме.
    /// Сбрасывается при деактивации режима выбора и при перезагрузке данных.
    /// </summary>
    private Dictionary<string, HashSet<int>> _groupChildIds = [];

    /// <summary>SQL-имена колонок текущей группировки.</summary>
    public IReadOnlyList<string> GroupColumns => _groupColumns.AsReadOnly();

    /// <summary>
    /// Возвращает <c>true</c> если колонка участвует в текущей группировке.
    /// </summary>
    public bool IsGrouped(string sqlName) => _groupColumns.Contains(sqlName);

    /// <summary>
    /// Возвращает порядок группировки для указанной SQL-колонки.
    /// Меньшее значение = внешний уровень группировки.
    /// </summary>
    public int GetGroupByOrder(string sqlColumn)
    {
        var idx = _groupColumns.IndexOf(sqlColumn);
        return idx < 0 ? 0 : idx;
    }

    private async Task AddGroupColumn(string column)
    {
        if (_groupColumns.Contains(column))
            return;
        _groupColumns.Add(column);
        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    private async Task RemoveGroupColumn(string column)
    {
        _groupColumns.Remove(column);
        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    /// <summary>
    /// Переключает развёрнутость всех групп на заданной глубине через DataLoader.
    /// </summary>
    private async Task ToggleLevel(int depth)
    {
        if (DataLoader is not null)
        {
            await DataLoader.ToggleLevelExpandedAsync(depth);
            StateHasChanged();
        }
    }

    /// <summary>
    /// Возвращает ID всех дочерних сущностей группы (из кеша или null).
    /// </summary>
    private HashSet<int>? GetChildIdsForGroup(string fullKey)
    {
        if (_groupChildIds.TryGetValue(fullKey, out var ids))
            return ids;
        return null;
    }

    /// <summary>
    /// Ленивая загрузка ID дочерних сущностей группы через DataLoader.
    /// </summary>
    private async Task LoadChildIdsForGroupsAsync(List<string> fullKeys)
    {
        if (DataLoader is null || fullKeys.Count == 0) return;
        var batch = await DataLoader.LoadGroupChildIdsAsync(fullKeys, _lastQuery);
        foreach (var kv in batch)
            _groupChildIds[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Сбрасывает кеш дочерних ID групп (вызывается перед перезагрузкой данных).
    /// </summary>
    private void ClearGroupChildCache()
    {
        if (_selectMode)
            _groupChildIds.Clear();
    }

    /// <summary>
    /// Tri-state чекбокса для группового заголовка.
    /// Все потомки выбраны → (true, false), ни одного → (false, false), часть → (false, true).
    /// </summary>
    private (bool Checked, bool Indeterminate) ComputeGroupCheckState(GroupHeaderRow gh)
    {
        var childIds = GetChildIdsForGroup(gh.FullKey);
        if (childIds is null || childIds.Count == 0)
            return (false, false);

        int selectedCount = 0;
        foreach (var id in childIds)
            if (_selectedIds.Contains(id))
                selectedCount++;

        if (selectedCount == 0) return (false, false);
        if (selectedCount == childIds.Count) return (true, false);
        return (false, true);
    }

    /// <summary>
    /// Обработчик клика по tri-state иконке в заголовке колонки выбора.
    /// Если есть выделенные (хотя бы один) → снимаем всё, иначе → выделяем всё.
    /// </summary>
    private async Task OnHeaderTriToggle()
    {
        bool anySelected = IsHeaderIndeterminate() || _selectAllChecked;
        _selectAllChecked = !anySelected;

        var missingKeys = new List<string>();
        foreach (var row in Items ?? [])
        {
            if (row is GroupHeaderRow gh && GetChildIdsForGroup(gh.FullKey) is null)
                missingKeys.Add(gh.FullKey);
        }
        if (missingKeys.Count > 0)
            await LoadChildIdsForGroupsAsync(missingKeys);

        foreach (var row in Items ?? [])
        {
            if (row is IDetailRow dr && dr.Item is Entity entity)
            {
                if (!anySelected) _selectedIds.Add(entity.Id);
                else _selectedIds.Remove(entity.Id);
            }
            else if (row is GroupHeaderRow gh)
            {
                var childIds = GetChildIdsForGroup(gh.FullKey);
                if (childIds is not null)
                {
                    foreach (var id in childIds)
                    {
                        if (!anySelected) _selectedIds.Add(id);
                        else _selectedIds.Remove(id);
                    }
                }
            }
        }

        StateHasChanged();
    }

    /// <summary>
    /// Обработчик клика по tri-state иконке группы.
    /// Если все потомки выбраны → снимаем всё; иначе → выделяем всё.
    /// </summary>
    private async Task OnGroupTriToggle(GroupHeaderRow gh)
    {
        var childIds = GetChildIdsForGroup(gh.FullKey);
        if (childIds is null)
        {
            await LoadChildIdsForGroupsAsync([gh.FullKey]);
            childIds = GetChildIdsForGroup(gh.FullKey);
        }
        if (childIds is null || childIds.Count == 0) return;

        bool allSelected = true;
        foreach (var id in childIds)
        {
            if (!_selectedIds.Contains(id)) { allSelected = false; break; }
        }

        foreach (var id in childIds)
        {
            if (allSelected) _selectedIds.Remove(id);
            else _selectedIds.Add(id);
        }
        _selectAllChecked = ComputeSelectAllState();
        StateHasChanged();
    }

    /// <summary>
    /// Обработчик клика по чекбоксу группы — выделяет/снимает всех потомков.
    /// При первом обращении лениво загружает ID дочерних сущностей через DataLoader.
    /// </summary>
    private async Task OnGroupSelectAsync(GroupHeaderRow gh, bool selected)
    {
        var childIds = GetChildIdsForGroup(gh.FullKey);
        if (childIds is null)
        {
            await LoadChildIdsForGroupsAsync([gh.FullKey]);
            childIds = GetChildIdsForGroup(gh.FullKey);
        }
        if (childIds is null) return;

        foreach (var id in childIds)
        {
            if (selected) _selectedIds.Add(id);
            else _selectedIds.Remove(id);
        }
        _selectAllChecked = ComputeSelectAllState();
        StateHasChanged();
    }

    /// <summary>
    /// Строит читаемое описание активной группировки для экспорта/печати.
    /// </summary>
    private string? BuildGroupDescription()
    {
        if (_groupColumns.Count == 0) return null;
        var names = _groupColumns
            .Select(c => _columnBySqlName.TryGetValue(c, out var m) ? m.DisplayName : c);
        return $"Группировка: {string.Join(" → ", names)}";
    }
}
