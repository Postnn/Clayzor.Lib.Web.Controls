using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Navigation;

/// <summary>Универсальное многоуровневое навигационное меню без зависимости от прикладной БД.</summary>
public partial class ClayNavigationMenu : IAsyncDisposable
{
    private readonly string _id = $"clay-nav-{Guid.NewGuid():N}";
    private readonly HashSet<string> _openPaths = new(StringComparer.Ordinal);
    private DotNetObjectReference<ClayNavigationMenu>? _reference;
    private IReadOnlyList<ClayNavigationMenuItem>? _previousItems;
    private bool _disposed;

    /// <summary>Корневые пункты меню.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ClayNavigationMenuItem> Items { get; set; } = [];

    /// <summary>Настройки взаимодействия и геометрии.</summary>
    [Parameter] public ClayNavigationMenuOptions Options { get; set; } = new();

    /// <summary>Дополнительные действия внутри корневой полосы меню, выровненные вправо.</summary>
    [Parameter] public RenderFragment? AdditionalContent { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_previousItems, Items)) return;
        _previousItems = Items;
        _openPaths.Clear();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;
        if (firstRender)
        {
            _reference = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("clayNavigationMenu.init", _id, _reference, Options.HoverDelayMs);
        }
        await JS.InvokeVoidAsync("clayNavigationMenu.refresh", _id, Options.ViewportMarginPx, Options.MaxPanelWidthPx);
    }

    internal bool IsOpen(string path) => _openPaths.Contains(path);
    internal string PanelId(string path) => $"{_id}-panel-{path}";
    internal string ItemId(string path) => $"{_id}-item-{path}";
    internal string RootId => _id;

    internal async Task ToggleAsync(string path)
    {
        if (IsOpen(path))
            ClosePath(path);
        else
            OpenPath(path);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Открывает панель по пути из JS без переключения её состояния.</summary>
    [JSInvokable]
    public async Task OpenAsync(string path)
    {
        OpenPath(path);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Закрывает панель и её потомков по таймеру pointer leave.</summary>
    [JSInvokable]
    public async Task ClosePanelAsync(string path)
    {
        ClosePath(path);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Закрывает все панели экземпляра.</summary>
    [JSInvokable]
    public async Task CloseAllAsync()
    {
        _openPaths.Clear();
        await InvokeAsync(StateHasChanged);
    }

    internal async Task ActivateAsync(ClayNavigationMenuItem item)
    {
        if (item.Disabled) return;
        _openPaths.Clear();
        if (item.OnActivate is not null)
            await item.OnActivate();
        await InvokeAsync(StateHasChanged);
    }

    internal static string? SafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        var value = href.Trim();
        return value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private void OpenPath(string path)
    {
        var depth = Depth(path);
        _openPaths.RemoveWhere(open => Depth(open) >= depth && !path.StartsWith(open + ".", StringComparison.Ordinal) && open != path);
        _openPaths.Add(path);
    }

    private void ClosePath(string path) =>
        _openPaths.RemoveWhere(open => open == path || open.StartsWith(path + ".", StringComparison.Ordinal));

    private static int Depth(string path) => path.Count(character => character == '.');

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await JS.InvokeVoidAsync("clayNavigationMenu.dispose", _id);
        }
        catch (Exception exception) when (exception is JSDisconnectedException or ObjectDisposedException or InvalidOperationException)
        {
        }
        _reference?.Dispose();
    }
}
