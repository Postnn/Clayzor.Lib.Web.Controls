using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Select;

public partial class ClaySelect<TItem, TValue>
{
    private const string EditorFocusModulePath = "./_content/Clayzor.Lib.Web.Controls/js/clayEditorFocus.js";
    private Task<IJSObjectReference>? _editorFocusModule;

    private async Task SelectConfirmedItemAsync(TItem item)
    {
        if (!_options.AdvanceFocusOnSelection)
        {
            await SelectItemAsync(item);
            return;
        }

        var version = _resolveVersion;
        var searchVersion = _searchVersion;
        var lease = await CaptureSelectionAdvanceAsync();
        if (_disposed || !IsMutable || version != _resolveVersion || searchVersion != _searchVersion ||
            !_open || _loading || !_results.Contains(item))
            return;

        var id = ValueSelector(item);
        await SelectItemAsync(item);
        if (IsAcceptedSelection(version + 1, id))
            await CompleteSelectionAdvanceAsync(lease);
    }

    private bool IsAcceptedSelection(int version, TValue? value) =>
        !_disposed && IsMutable && _resolveVersion == version && !HasErrors &&
        EqualityComparer<TValue?>.Default.Equals(Value, value);

    private async Task<long?> CaptureSelectionAdvanceAsync()
    {
        if (!_options.AdvanceFocusOnSelection || _disposed)
            return null;
        try
        {
            _editorFocusModule ??= JS.InvokeAsync<IJSObjectReference>("import", EditorFocusModulePath).AsTask();
            var module = await _editorFocusModule;
            return _disposed ? null : await module.InvokeAsync<long?>("captureSelectionAdvance", _inputRef);
        }
        catch (Exception error) when (IsFocusInteropFailure(error))
        {
            return null;
        }
    }

    private async Task CompleteSelectionAdvanceAsync(long? lease)
    {
        if (_disposed || !_options.AdvanceFocusOnSelection || lease is not { } id || _editorFocusModule is not { } module)
            return;
        try { await (await module).InvokeVoidAsync("completeSelectionAdvance", _inputRef, id); }
        catch (Exception error) when (IsFocusInteropFailure(error)) { }
    }

    private async ValueTask ReleaseSelectionFocusAsync()
    {
        if (_editorFocusModule is not { } module)
            return;
        _editorFocusModule = null;
        try { await (await module).DisposeAsync(); }
        catch (Exception error) when (IsFocusInteropFailure(error)) { }
    }

    private static bool IsFocusInteropFailure(Exception error) =>
        error is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException;
}
