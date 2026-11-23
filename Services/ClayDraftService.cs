using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Восстановленное состояние черновика текущей формы.</summary>
public sealed record ClayDraftRestore(
    string RegistrationId,
    bool HasDraft,
    long Revision,
    string? TypedState,
    string? Operation);

/// <summary>Общий DR-01 API долговечного клиентского черновика и snapshot бизнес-операции.</summary>
public sealed class ClayDraftService(IJSRuntime js) : IAsyncDisposable
{
    private string? _registrationId;

    /// <summary>Регистрирует изолированный draft пользователя, формы, сущности и вкладки.</summary>
    public async Task<ClayDraftRestore?> RegisterAsync(
        string formKey,
        string entityKey,
        string userKey,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        if (_registrationId is not null)
            throw new InvalidOperationException("Черновик уже зарегистрирован для текущего circuit.");
        var restore = await js.InvokeAsync<ClayDraftRestore?>(
            "clayDraftStore.register", cancellationToken, formKey, entityKey, userKey, schemaVersion);
        if (restore is null)
            return null;
        _registrationId = restore.RegistrationId;
        return restore;
    }

    /// <summary>Сохраняет типизированное не секретное состояние после подтверждения клиентского store.</summary>
    public async Task<long> SaveTypedAsync(string typedState, CancellationToken cancellationToken = default)
    {
        var id = RequireRegistration();
        return await js.InvokeAsync<long>("clayDraftStore.saveTyped", cancellationToken, id, typedState);
    }

    /// <summary>Применяет сырой ввод к восстановленному DOM без бизнес-команды.</summary>
    public async Task ApplyRawAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireRegistration();
        await js.InvokeVoidAsync("clayDraftStore.applyRaw", cancellationToken, id);
    }

    /// <summary>Фиксирует snapshot команды до её отправки и возвращает отправляемую ревизию.</summary>
    public async Task<long> BeginOperationAsync(string operation, CancellationToken cancellationToken = default)
    {
        var id = RequireRegistration();
        return await js.InvokeAsync<long>("clayDraftStore.beginOperation", cancellationToken, id, operation);
    }

    /// <summary>Снимает running-snapshot после подтверждённого отказа до записи.</summary>
    public async Task RejectOperationAsync(long revision, CancellationToken cancellationToken = default) =>
        await InvokeOperation("clayDraftStore.rejectOperation", revision, cancellationToken);

    /// <summary>Сохраняет неизвестный исход для последующей сверки без разрешения replay.</summary>
    public async Task UnknownOperationAsync(long revision, CancellationToken cancellationToken = default) =>
        await InvokeOperation("clayDraftStore.unknownOperation", revision, cancellationToken);

    /// <summary>Очищает только принятую ревизию и обновляет baseline без потери более позднего ввода.</summary>
    public async Task CommitOperationAsync(
        long revision,
        string? cleanTypedState = null,
        CancellationToken cancellationToken = default)
    {
        var id = RequireRegistration();
        await js.InvokeVoidAsync("clayDraftStore.commitOperation", cancellationToken, id, revision, cleanTypedState);
    }

    /// <summary>Явно отбрасывает черновик и запрещает его восстановление поздним autosave.</summary>
    public async Task DiscardAsync(CancellationToken cancellationToken = default)
    {
        var id = RequireRegistration();
        await js.InvokeVoidAsync("clayDraftStore.discard", cancellationToken, id);
        _registrationId = null;
    }

    private async Task InvokeOperation(string method, long revision, CancellationToken cancellationToken)
    {
        var id = RequireRegistration();
        await js.InvokeVoidAsync(method, cancellationToken, id, revision);
    }

    private string RequireRegistration() =>
        _registrationId ?? throw new InvalidOperationException("Черновик ещё не зарегистрирован.");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_registrationId is null)
            return;
        try
        {
            await js.InvokeVoidAsync("clayDraftStore.unregister", _registrationId);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException)
        {
        }
        _registrationId = null;
    }
}
