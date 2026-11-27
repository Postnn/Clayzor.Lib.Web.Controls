using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Form;

internal sealed class ClayPopupPickerFocus<T>(IJSRuntime js) : IAsyncDisposable where T : struct
{
    private readonly ClayEditorFocusClient _client = new(js);
    private int _generation;
    private (long Request, T Value)? _submitted;

    internal string Key => _client.Key;

    internal void Opened()
    {
        _generation++;
        _submitted = null;
    }

    internal async Task SubmittedAsync(T? value)
    {
        if (value is not { } candidate || _submitted is not null) return;
        var generation = _generation;
        var request = await _client.ClaimPopupSelectionAsync();
        if (generation == _generation && request is { } id)
            _submitted = (id, candidate);
    }

    internal async Task ClosedAsync(T? accepted, bool canAdvance)
    {
        var submitted = _submitted;
        Opened();
        if (canAdvance && submitted is { } result && EqualityComparer<T?>.Default.Equals(result.Value, accepted))
            await _client.CompletePopupSelectionAsync(result.Request);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Opened();
        return _client.DisposeAsync();
    }
}
