using System.Collections.Concurrent;
using System.Text.Json;
using Clayzor.Lib.Web.Controls.Components.Select;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>
/// Централизованно открывает внешнюю форму расширенного поиска и возвращает выбранный ID
/// в <see cref="ClaySelect{TItem, TValue}"/> через защищённый postMessage с requestId.
/// </summary>
public sealed class ClaySelectAdvancedSearchService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions KeyJson = new() { PropertyNameCaseInsensitive = true };
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly ConcurrentDictionary<string, AcknowledgedRequest> _acknowledged = new();
    private readonly DotNetObjectReference<ClaySelectAdvancedSearchService> _dotnetRef;
    private bool _disposed;

    /// <summary>
    /// Создаёт сервис возврата значений для текущего Blazor circuit.
    /// </summary>
    public ClaySelectAdvancedSearchService(IJSRuntime js)
    {
        JS = js;
        _dotnetRef = DotNetObjectReference.Create(this);
    }

    private IJSRuntime JS { get; }

    /// <summary>
    /// Открывает URL внешней поисковой формы в новом окне и ожидает выбор или отмену.
    /// Форма должна вызвать общий JavaScript-метод <c>claySelectAdvancedSearch.returnValue</c>.
    /// </summary>
    /// <typeparam name="TItem">Тип элемента целевого ClaySelect.</typeparam>
    /// <typeparam name="TValue">Nullable-тип идентификатора элемента.</typeparam>
    /// <param name="url">Абсолютный URL формы поиска.</param>
    /// <param name="cancellationToken">Токен отмены ожидания результата.</param>
    /// <returns>Идентификатор для восстановления через источник или null при отмене.</returns>
    public async Task<ClaySelectExternalResult<TItem, TValue>?> OpenAsync<TItem, TValue>(
        string url,
        CancellationToken cancellationToken = default)
        where TItem : class
    {
        var value = await OpenCoreAsync(url, false, cancellationToken);
        if (value is null) return null;
        var id = value.Value.Deserialize<TValue>(KeyJson);
        if (id is null) throw new InvalidOperationException("Внешняя форма вернула пустой идентификатор.");
        return ClaySelectExternalResult<TItem, TValue>.FromValue(id);
    }

    /// <summary>Возвращает подтверждённый набор целочисленных ключей, включая пустой. Отмена возвращает null.</summary>
    public async Task<int[]?> OpenMultipleIdsAsync(string url, CancellationToken cancellationToken = default)
    {
        var value = await OpenCoreAsync(url, true, cancellationToken);
        if (value is null) return null;
        return ReadIds(value.Value);
    }

    /// <summary>Открывает единичный поиск с подтверждением после фактического применения родителем.</summary>
    public Task<bool> OpenAcknowledgedAsync<TItem, TValue>(
        string url,
        Func<ClaySelectExternalResult<TItem, TValue>, CancellationToken, Task<ClaySelectReturnDecision>> apply,
        CancellationToken cancellationToken = default)
        where TItem : class =>
        OpenAcknowledgedCoreAsync(url, false, async (value, token) =>
        {
            TValue? id;
            try { id = value.Deserialize<TValue>(KeyJson); }
            catch (JsonException) { return ClaySelectReturnDecision.Rejected; }
            return id is null ? ClaySelectReturnDecision.Rejected
                : await apply(ClaySelectExternalResult<TItem, TValue>.FromValue(id), token);
        }, cancellationToken);

    /// <summary>Открывает множественный поиск, в котором пустой набор применяется как явный выбор.</summary>
    public Task<bool> OpenAcknowledgedMultipleIdsAsync(
        string url, Func<int[], CancellationToken, Task<ClaySelectReturnDecision>> apply,
        CancellationToken cancellationToken = default) =>
        OpenAcknowledgedCoreAsync(url, true, async (value, token) =>
        {
            int[] ids;
            try { ids = ReadIds(value); }
            catch (JsonException) { return ClaySelectReturnDecision.Rejected; }
            return await apply(ids, token);
        }, cancellationToken);

    private static int[] ReadIds(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 1000)
            throw new JsonException("Недопустимый набор идентификаторов.");
        var ids = value.Deserialize<int[]>()!;
        if (ids.Any(id => id <= 0)) throw new JsonException("Недопустимый идентификатор.");
        return ids.Distinct().Order().ToArray();
    }

    private async Task<bool> OpenAcknowledgedCoreAsync(string url, bool multiple,
        Func<JsonElement, CancellationToken, Task<ClaySelectReturnDecision>> apply, CancellationToken cancellationToken)
    {
        CheckUrl(url);
        var requestId = Guid.NewGuid().ToString("N");
        var request = new AcknowledgedRequest(apply, cancellationToken);
        if (!_acknowledged.TryAdd(requestId, request))
            throw new InvalidOperationException("Не удалось зарегистрировать запрос расширенного поиска.");

        var accepted = false;
        try
        {
            await JS.InvokeVoidAsync("claySelectAdvancedSearch.open", cancellationToken, url, requestId, _dotnetRef, multiple, true);
            accepted = await request.Completion.Task.WaitAsync(cancellationToken);
            return accepted;
        }
        finally
        {
            if (!accepted)
            {
                if (_acknowledged.TryRemove(requestId, out var cancelled))
                    cancelled.Cancel();
                await CancelBrowserRequestAsync(requestId);
            }
        }
    }

    /// <summary>Проверяет и применяет одну ревизию результата; повтор подтверждённой ревизии не применяет её заново.</summary>
    [JSInvokable]
    public async Task<string> ApplyAsync(string requestId, int revision, JsonElement value)
    {
        if (revision < 1 || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
            !_acknowledged.TryGetValue(requestId, out var request))
            return nameof(ClaySelectReturnDecision.Stale);

        await request.Gate.WaitAsync();
        try
        {
            if (request.Token.IsCancellationRequested || !_acknowledged.ContainsKey(requestId))
                return nameof(ClaySelectReturnDecision.Stale);

            var json = value.GetRawText();
            if (revision == request.Revision && json == request.Json)
                return request.Decision.ToString();

            if (revision != request.Revision + 1 || request.Decision != ClaySelectReturnDecision.Rejected)
                return nameof(ClaySelectReturnDecision.Stale);

            request.Revision = revision;
            request.Json = json;
            try
            {
                request.Decision = await request.Apply(value, request.Token);
            }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
            {
                return nameof(ClaySelectReturnDecision.Stale);
            }
            catch (Exception)
            {
                // Callback мог изменить форму до сбоя. Не воспроизводим такую попытку.
                request.Decision = ClaySelectReturnDecision.Indeterminate;
            }

            if (request.Token.IsCancellationRequested)
                return nameof(ClaySelectReturnDecision.Stale);

            if (request.Decision == ClaySelectReturnDecision.Accepted)
                request.Completion.TrySetResult(true);

            return request.Decision.ToString();
        }
        finally { request.Gate.Release(); }
    }

    private sealed class AcknowledgedRequest
    {
        private readonly CancellationTokenSource _lifetime;
        internal Func<JsonElement, CancellationToken, Task<ClaySelectReturnDecision>> Apply { get; }
        internal CancellationToken Token { get; }
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Revision { get; set; }
        internal string? Json { get; set; }
        internal ClaySelectReturnDecision Decision { get; set; } = ClaySelectReturnDecision.Rejected;

        internal AcknowledgedRequest(Func<JsonElement, CancellationToken, Task<ClaySelectReturnDecision>> apply, CancellationToken token)
        {
            Apply = apply;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = _lifetime.Token;
        }

        internal void Cancel()
        {
            try { _lifetime.Cancel(); }
            finally
            {
                _lifetime.Dispose();
                Completion.TrySetResult(false);
            }
        }
    }

    private async Task<JsonElement?> OpenCoreAsync(string url, bool multiple, CancellationToken cancellationToken)
    {
        CheckUrl(url);
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("Не удалось зарегистрировать запрос расширенного поиска.");

        try
        {
            await JS.InvokeVoidAsync("claySelectAdvancedSearch.open", cancellationToken, url, requestId, _dotnetRef, multiple);
            var value = await completion.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return value;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
            await CancelBrowserRequestAsync(requestId);
        }
    }

    private void CheckUrl(string url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            throw new ArgumentException("URL расширенного поиска должен быть абсолютным.", nameof(url));
    }

    /// <summary>
    /// Принимает выбранное значение из проверенного браузерного сообщения.
    /// </summary>
    [JSInvokable]
    public Task CompleteAsync(string requestId, JsonElement value)
    {
        if (_pending.TryGetValue(requestId, out var completion))
            completion.TrySetResult(value);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Завершает ожидание без изменения значения при закрытии окна или отмене.
    /// </summary>
    [JSInvokable]
    public Task CancelAsync(string requestId)
    {
        if (_pending.TryGetValue(requestId, out var completion))
            completion.TrySetResult(null);
        if (_acknowledged.TryRemove(requestId, out var request))
            request.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Закрывает дочерние окна, удаляет браузерные обработчики и завершает все ожидания.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var requestId in _pending.Keys.Concat(_acknowledged.Keys))
        {
            await CancelBrowserRequestAsync(requestId);
            await CancelAsync(requestId);
        }
        _dotnetRef.Dispose();
    }

    private async Task CancelBrowserRequestAsync(string requestId)
    {
        try
        {
            await JS.InvokeVoidAsync("claySelectAdvancedSearch.cancel", requestId);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
