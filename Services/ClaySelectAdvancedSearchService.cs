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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly DotNetObjectReference<ClaySelectAdvancedSearchService> _dotnetRef;

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
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException("URL расширенного поиска должен быть абсолютным.", nameof(url));

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("Не удалось зарегистрировать запрос расширенного поиска.");

        try
        {
            await JS.InvokeVoidAsync("claySelectAdvancedSearch.open", cancellationToken, url, requestId, _dotnetRef);
            await using var registration = cancellationToken.Register(() => CancelAsync(requestId));
            var value = await completion.Task;
            if (value is null)
                return null;

            var id = value.Value.Deserialize<TValue>();
            if (id is null)
                throw new InvalidOperationException("Внешняя форма вернула пустой идентификатор.");
            return ClaySelectExternalResult<TItem, TValue>.FromValue(id);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
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
        return Task.CompletedTask;
    }

    /// <summary>
    /// Отменяет все ожидания и освобождает ссылку на сервис для JavaScript.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        foreach (var requestId in _pending.Keys)
            CancelAsync(requestId);
        _dotnetRef.Dispose();
        return ValueTask.CompletedTask;
    }
}
