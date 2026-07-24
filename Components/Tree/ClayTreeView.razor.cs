using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.DataSources;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Clayzor.Lib.Web.Controls.Components.Tree.State;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Дерево с серверной ленивой загрузкой уровней. Поддерживает две модели иерархии:
/// <see cref="ClayTreeHierarchyMode.NestedSet"/> (вложенные множества) и
/// <see cref="ClayTreeHierarchyMode.ParentKey"/> (ссылка на родителя).
/// </summary>
public partial class ClayTreeView : ComponentBase, IClayTreeView
{
    // ── Parameters ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Настройки дерева. Обязателен — без <see cref="ClayTreeOptions.TreeId"/>
    /// и <see cref="ClayTreeOptions.SelectSql"/> компонент не имеет смысла.
    /// </summary>
    [Parameter, EditorRequired] public ClayTreeOptions Options { get; set; } = null!;

    /// <summary>Подменный источник данных (тесты, нестандартные источники).</summary>
    [Parameter] public IClayTreeDataSource? DataSource { get; set; }

    /// <summary>Своя разметка узла. Если не задан — стандартный текст.</summary>
    [Parameter] public RenderFragment<ClayTreeNode>? NodeTemplate { get; set; }

    /// <summary>Событие клика по тексту узла.</summary>
    [Parameter] public EventCallback<ClayTreeNode> OnNodeClick { get; set; }

    /// <summary>Событие раскрытия узла.</summary>
    [Parameter] public EventCallback<ClayTreeNode> OnNodeExpanded { get; set; }

    /// <summary>Событие сворачивания узла.</summary>
    [Parameter] public EventCallback<ClayTreeNode> OnNodeCollapsed { get; set; }

    /// <summary>Событие ошибки загрузки уровня.</summary>
    [Parameter] public EventCallback<string> OnLoadError { get; set; }

    // ── Injects ──────────────────────────────────────────────────────────────────

    [Inject] private DbManager Db { get; set; } = default!;
    [Inject] private IClayTreeStateStore StateStore { get; set; } = default!;

    // ── Fields ───────────────────────────────────────────────────────────────────

    private ClayTreeSource? _source;
    private IClayTreeDataSource _dataSource = null!;
    private readonly List<ClayTreeNode> _roots = [];
    private string? _error;
    private bool _initialLoading;

    // ── IClayTreeView ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string TreeId => Options.TreeId;

    /// <inheritdoc/>
    public ClayTreeHierarchyMode HierarchyMode => Options.HierarchyMode;

    /// <inheritdoc/>
    public IReadOnlyList<ClayTreeNode> RootNodes => _roots;

    /// <inheritdoc/>
    public int IndentPx => Options.IndentPx;

    /// <inheritdoc/>
    async Task IClayTreeView.ReloadAsync() => await LoadRootsAsync();

    /// <inheritdoc/>
    async Task IClayTreeView.ExpandAsync(string id) => await ExpandNodeAsync(id);

    /// <inheritdoc/>
    async Task IClayTreeView.CollapseAsync(string id) => await CollapseNodeAsync(id);

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrWhiteSpace(Options.TreeId))
            throw new InvalidOperationException("ClayTreeOptions.TreeId не задан — идентификатор дерева обязателен.");
        if (string.IsNullOrWhiteSpace(Options.SelectSql))
            throw new InvalidOperationException("ClayTreeOptions.SelectSql не задан — источник данных дерева обязателен.");

        Options.Schema.Validate(Options.HierarchyMode);

        _source = new ClayTreeSource(
            Options.SelectSql,
            Options.HierarchyMode,
            Options.Schema,
            Options.OrderBy,
            Options.RootId);

        _dataSource = DataSource ?? new ClaySqlTreeDataSource(Db, _source);
    }

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        // Сравнение значимых значений для детекта смены источника
        var selectSql = Options.SelectSql;
        var mode = Options.HierarchyMode;
        var rootId = Options.RootId;

        if (_source is not null &&
            (_source.SelectSql != selectSql || _source.Mode != mode || _source.RootId != rootId))
        {
            _source = new ClayTreeSource(selectSql, mode, Options.Schema, Options.OrderBy, rootId);
            _dataSource = DataSource ?? new ClaySqlTreeDataSource(Db, _source);
            await LoadRootsAsync();
        }
    }
}
