using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    [Inject] private IJSRuntime KeyboardJs { get; set; } = null!;

    private ElementReference _keyboardRoot;
    private DotNetObjectReference<ClayTreeView>? _keyboardReference;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        _keyboardReference = DotNetObjectReference.Create(this);
        try
        {
            await KeyboardJs.InvokeVoidAsync("clayTree.attachKeyboard", _keyboardRoot, _keyboardReference);
        }
        catch (JSDisconnectedException) { }
    }

    /// <summary>Применяет клавиатурную команду только к узлу видимой ветки текущего дерева.</summary>
    [JSInvokable]
    public async Task InvokeKeyboardActionAsync(string id, string action)
    {
        if (_keyboardReference is null || _isBusy || !_byId.TryGetValue(id, out var node))
            return;

        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.IsExpanded)
                return;
        }

        switch (action)
        {
            case "activate":
                await HandleNodeClick(node);
                break;
            case "expand" when !node.IsExpanded && !node.IsLoading &&
                               (node.HasChildren || node.ChildrenAreFiltered && node.Children.Count > 0):
                await ExpandNodeAsync(id);
                break;
            case "collapse" when node.IsExpanded && !node.IsLoading:
                await CollapseNodeAsync(id);
                break;
            case "check" when Options is { SelectionMode: ClayTreeSelectionMode.Multiple, IsChecked: not null, ToggleCheckedAsync: { } toggle }:
                await toggle(node);
                StateHasChanged();
                break;
        }
    }

    private async Task ReleaseKeyboardAsync()
    {
        if (_keyboardReference is not { } reference)
            return;

        _keyboardReference = null;
        reference.Dispose();
        try
        {
            await KeyboardJs.InvokeVoidAsync("clayTree.detachKeyboard", _keyboardRoot);
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
