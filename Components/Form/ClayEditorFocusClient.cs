using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Form;

internal sealed class ClayEditorFocusClient(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./_content/Clayzor.Lib.Web.Controls/js/clayEditorFocus.js";
    private Task<IJSObjectReference>? _module;
    private bool _disposed;
    internal string Key { get; } = Guid.NewGuid().ToString("N");

    internal async Task<long?> ClaimAsync(bool alreadySelected = false)
    {
        if (_disposed) return null;
        try
        {
            return await (await ModuleAsync()).InvokeAsync<long?>("claimNativeSelectionAdvance", Key, alreadySelected);
        }
        catch (Exception error) when (IsInteropFailure(error)) { return null; }
    }

    internal async Task CompleteAsync(long? lease)
    {
        if (_disposed || lease is not { } id || _module is not { } module) return;
        try { await (await module).InvokeVoidAsync("completeNativeSelectionAdvance", Key, id); }
        catch (Exception error) when (IsInteropFailure(error)) { }
    }

    internal async Task<ClayTextEntry?> ClaimTextEntryAsync()
    {
        if (_disposed) return null;
        try { return await (await ModuleAsync()).InvokeAsync<ClayTextEntry?>("claimTextEntry", Key); }
        catch (Exception error) when (IsInteropFailure(error)) { return null; }
    }

    internal async Task CompleteTextEntryAsync(long lease)
    {
        if (_disposed || _module is not { } module) return;
        try { await (await module).InvokeVoidAsync("completeTextEntry", Key, lease); }
        catch (Exception error) when (IsInteropFailure(error)) { }
    }

    internal async Task<long?> ClaimPopupSelectionAsync()
    {
        if (_disposed) return null;
        try { return await (await ModuleAsync()).InvokeAsync<long?>("claimPopupSelection", Key); }
        catch (Exception error) when (IsInteropFailure(error)) { return null; }
    }

    internal async Task CompletePopupSelectionAsync(long lease)
    {
        if (_disposed || _module is not { } module) return;
        try { await (await module).InvokeVoidAsync("completePopupSelection", Key, lease); }
        catch (Exception error) when (IsInteropFailure(error)) { }
    }

    private Task<IJSObjectReference> ModuleAsync() =>
        _module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is not { } module) return;
        try { await (await module).DisposeAsync(); }
        catch (Exception error) when (IsInteropFailure(error)) { }
    }

    private static bool IsInteropFailure(Exception error) =>
        error is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException;
}

internal sealed record ClayTextEntry(long Id, string Value);
