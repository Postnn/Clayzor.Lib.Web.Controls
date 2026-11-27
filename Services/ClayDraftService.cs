using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Восстановленное состояние черновика конкретной регистрации.</summary>
public sealed record ClayDraftRestore(
    [property: JsonPropertyName("registrationId")] string RegistrationId,
    [property: JsonPropertyName("hasDraft")] bool HasDraft,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("typedState")] string? TypedState,
    [property: JsonPropertyName("typedScopes")] IReadOnlyDictionary<string, string>? TypedScopes = null,
    [property: JsonPropertyName("cancelledScopes")] IReadOnlyList<string>? CancelledScopes = null);

/// <summary>Результат ожидаемого raw restore; pending ключи остаются в защищённом draft.</summary>
public sealed record ClayDraftRawApplyResult(
    [property: JsonPropertyName("applied")] IReadOnlyList<string> Applied,
    [property: JsonPropertyName("pending")] IReadOnlyList<string> Pending,
    [property: JsonPropertyName("scopes")] IReadOnlyDictionary<string, ClayDraftRawScopeApplyResult>? Scopes = null);

/// <summary>Raw restore одной editor scope без раскрытия значений полей.</summary>
public sealed record ClayDraftRawScopeApplyResult(
    [property: JsonPropertyName("applied")] IReadOnlyList<string> Applied,
    [property: JsonPropertyName("pending")] IReadOnlyList<string> Pending);

/// <summary>Readiness восстановления по именам областей; значения и секреты не покидают защищённый store.</summary>
public sealed record ClayDraftRestoreReadiness(
    [property: JsonPropertyName("complete")] bool Complete,
    [property: JsonPropertyName("pendingScopes")] IReadOnlyList<string> PendingScopes,
    [property: JsonPropertyName("failedScopes")] IReadOnlyList<string> FailedScopes);

/// <summary>Health регистрации для command/reload gates.</summary>
public sealed record ClayDraftStorageHealth(
    [property: JsonPropertyName("healthy")] bool Healthy,
    [property: JsonPropertyName("pendingWrites")] int PendingWrites,
    [property: JsonPropertyName("lastError")] string? LastError,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("persistedRevision")] long PersistedRevision);

/// <summary>Версия и ключи незакоммиченного ввода без его значений.</summary>
public sealed record ClayDraftRebaseState(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("rawKeys")] IReadOnlyList<string> RawKeys);


/// <summary>Ревизия и состояние редактируемой области, показанной в запросе отмены.</summary>
public sealed record ClayDraftEditorScopeState(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("hasRaw")] bool HasRaw);

/// <summary>Ревизия всего экземпляра и признаки ввода во всех областях перед закрытием.</summary>
public sealed record ClayDraftEditorCloseState(
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("hasRaw")] bool HasRaw,
    [property: JsonPropertyName("typedScopes")] IReadOnlyList<string> TypedScopes);

/// <summary>Метаданные отдельного черновика из текущего контекста, без значений пользовательских полей.</summary>
public sealed record ClayDraftRecoveryCandidate(
    [property: JsonPropertyName("draftId")] string DraftId,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("canRecover")] bool CanRecover);

internal sealed record ClayDraftRestoreStream(
    [property: JsonPropertyName("registrationId")] string RegistrationId,
    [property: JsonPropertyName("payload")] IJSStreamReference Payload);

/// <summary>Общий DR-01 API клиентского черновика полей и его ревизий.</summary>
public sealed class ClayDraftService(IJSRuntime js) : IAsyncDisposable
{
    private readonly Dictionary<string, ClayDraftRegistration> _registrations = new(StringComparer.Ordinal);
    private string? _registrationId;

    /// <summary>Признак наличия хотя бы одной активной регистрации в текущем circuit.</summary>
    public bool HasActiveRegistration => _registrations.Count > 0;

    /// <summary>Регистрирует draft и делает его текущим для обратно совместимых вызовов без handle.</summary>
    public async Task<ClayDraftRestore?> RegisterAsync(
        string formKey,
        string entityKey,
        string userKey,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var registration = await RegisterCoreAsync(formKey, entityKey, userKey, schemaVersion, null, cancellationToken);
        _registrationId = registration.RegistrationId;
        return registration.Restore;
    }

    /// <summary>Регистрирует draft с явным CSS root; selector может охватывать форму и её portal dialogs.</summary>
    public async Task<ClayDraftRestore?> RegisterAsync(
        string formKey,
        string entityKey,
        string userKey,
        int schemaVersion,
        string rootSelector,
        CancellationToken cancellationToken = default)
    {
        var registration = await RegisterCoreAsync(formKey, entityKey, userKey, schemaVersion, rootSelector, cancellationToken);
        _registrationId = registration.RegistrationId;
        return registration.Restore;
    }

    /// <summary>Создаёт независимый handle; один scoped service может владеть несколькими формами/editors.</summary>
    public async Task<ClayDraftRegistration> RegisterHandleAsync(
        string formKey,
        string entityKey,
        string userKey,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var registration = await RegisterCoreAsync(formKey, entityKey, userKey, schemaVersion, null, cancellationToken);
        _registrationId ??= registration.RegistrationId;
        return registration;
    }

    /// <summary>Создаёт handle с собственным DOM root, не пересекающимся с другими формами и entities.</summary>
    public async Task<ClayDraftRegistration> RegisterHandleAsync(
        string formKey,
        string entityKey,
        string userKey,
        int schemaVersion,
        string rootSelector,
        CancellationToken cancellationToken = default)
    {
        var registration = await RegisterCoreAsync(formKey, entityKey, userKey, schemaVersion, rootSelector, cancellationToken);
        _registrationId ??= registration.RegistrationId;
        return registration;
    }

    /// <summary>Создаёт независимую регистрацию, передающую большие состояния восстановления потоком.</summary>
    public async Task<ClayDraftRegistration> RegisterHandleAsync(
        string formKey, string entityKey, string userKey, int schemaVersion, string rootSelector,
        long maximumRestoreBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRestoreBytes);
        var registration = await RegisterCoreAsync(formKey, entityKey, userKey, schemaVersion,
            rootSelector, cancellationToken, maximumRestoreBytes);
        _registrationId ??= registration.RegistrationId;
        return registration;
    }

    /// <summary>Подключает экземпляр формы к серверному ключу, удерживаемому документом в RAM.</summary>
    public async Task<ClayDraftRegistration> RegisterRamHandleAsync(
        string formKey, string entityKey, string userKey, int schemaVersion, string rootSelector,
        Guid editorInstanceId, CancellationToken cancellationToken = default, long? maximumRestoreBytes = null)
    {
        if (editorInstanceId == Guid.Empty)
            throw new ArgumentException("Требуется идентификатор экземпляра формы.", nameof(editorInstanceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootSelector);
        if (maximumRestoreBytes is { } limit)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var registration = await RegisterCoreAsync(formKey, entityKey, userKey, schemaVersion,
            rootSelector, cancellationToken, maximumRestoreBytes, editorInstanceId);
        _registrationId ??= registration.RegistrationId;
        return registration;
    }

    private async Task<ClayDraftRegistration> RegisterCoreAsync(
        string formKey,
        string entityKey,
        string userKey,
        int schemaVersion,
        string? rootSelector,
        CancellationToken cancellationToken, long? maximumRestoreBytes = null, Guid? editorInstanceId = null)
    {
        if (rootSelector is not null && string.IsNullOrWhiteSpace(rootSelector))
            throw new ArgumentException("Draft DOM root selector is required.", nameof(rootSelector));
        var isRam = editorInstanceId.HasValue;
        object?[] args = editorInstanceId is { } instance
            ? [formKey, entityKey, userKey, schemaVersion, rootSelector, instance.ToString("D")]
            : rootSelector is null
                ? [formKey, entityKey, userKey, schemaVersion]
                : [formKey, entityKey, userKey, schemaVersion, rootSelector];
        var restore = maximumRestoreBytes is { } limit
            ? await RegisterStreamedRestoreAsync(isRam ? "clayDraftStore.registerRamStream" : "clayDraftStore.registerStream",
                args, limit, cancellationToken)
            : await js.InvokeAsync<ClayDraftRestore?>(isRam ? "clayDraftStore.registerRam" : "clayDraftStore.register",
                cancellationToken, args);
        if (restore is null || string.IsNullOrWhiteSpace(restore.RegistrationId))
            throw new InvalidOperationException("Draft store did not confirm registration; commands remain blocked.");
        if (_registrations.ContainsKey(restore.RegistrationId))
            throw new InvalidOperationException("Draft store returned a duplicate registration ID.");
        try
        {
            var registration = new ClayDraftRegistration(this, restore, maximumRestoreBytes, isRam);
            _registrations.Add(restore.RegistrationId, registration);
            return registration;
        }
        catch
        {
            try
            {
                await js.InvokeVoidAsync("clayDraftStore.unregister", restore.RegistrationId);
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
            {
            }
            throw;
        }
    }

    private async Task<ClayDraftRestore?> RegisterStreamedRestoreAsync(string method, object?[] args,
        long maximumRestoreBytes, CancellationToken token)
    {
        var envelope = await js.InvokeAsync<ClayDraftRestoreStream?>(method, token, args);
        if (envelope is null)
            return null;
        try { return await ReadStreamedRestoreAsync(envelope, maximumRestoreBytes, token); }
        catch
        {
            if (!string.IsNullOrWhiteSpace(envelope.RegistrationId))
            {
                try { await js.InvokeVoidAsync("clayDraftStore.unregister", envelope.RegistrationId); }
                catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException) { }
            }
            throw;
        }
    }

    private static async Task<ClayDraftRestore> ReadStreamedRestoreAsync(
        ClayDraftRestoreStream envelope, long maximumRestoreBytes, CancellationToken token)
    {
        try
        {
            if (envelope.Payload is null || string.IsNullOrWhiteSpace(envelope.RegistrationId))
                throw new InvalidOperationException("Контекст восстановления не подтверждён.");
            await using var reference = envelope.Payload;
            await using var stream = await reference.OpenReadStreamAsync(maximumRestoreBytes, token);
            var restore = await JsonSerializer.DeserializeAsync<ClayDraftRestore>(stream, cancellationToken: token);
            return restore is not null && restore.RegistrationId == envelope.RegistrationId
                ? restore : throw new InvalidOperationException("Контекст восстановления не подтверждён.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or InvalidOperationException or JSException or JSDisconnectedException)
        {
            throw new InvalidOperationException("Не удалось прочитать защищённые данные черновика.");
        }
    }

    /// <summary>Сохраняет legacy typed состояние текущей регистрации.</summary>
    public Task<long> SaveTypedAsync(string typedState, CancellationToken cancellationToken = default) =>
        SaveTypedAsync(RequireRegistration().RegistrationId, typedState, cancellationToken);

    /// <summary>Сохраняет legacy typed состояние указанной регистрации.</summary>
    public async Task<long> SaveTypedAsync(string registrationId, string typedState, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<long>("clayDraftStore.saveTyped", cancellationToken, registrationId, typedState);
    }

    /// <summary>Сохраняет typed состояние отдельной области текущей регистрации.</summary>
    public Task<long> SaveTypedScopeAsync(string scope, string typedState, CancellationToken cancellationToken = default) =>
        SaveTypedScopeAsync(RequireRegistration().RegistrationId, scope, typedState, cancellationToken);

    /// <summary>Сохраняет typed состояние отдельной области указанной регистрации.</summary>
    public async Task<long> SaveTypedScopeAsync(string registrationId, string scope, string typedState, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<long>("clayDraftStore.saveTypedScope", cancellationToken, registrationId, scope, typedState);
    }

    /// <summary>Ожидает применение raw ввода текущей регистрации.</summary>
    public Task ApplyRawAsync(CancellationToken cancellationToken = default) =>
        ApplyRawAsync(RequireRegistration().RegistrationId, cancellationToken);

    /// <summary>Ожидает применение raw ввода указанной регистрации.</summary>
    public async Task ApplyRawAsync(string registrationId, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        await js.InvokeVoidAsync("clayDraftStore.applyRaw", cancellationToken, registrationId);
    }

    /// <summary>Возвращает ключи, ожидающие DOM/readiness, после полного await raw restore.</summary>
    public async Task<ClayDraftRawApplyResult> ApplyRawWithResultAsync(string registrationId, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<ClayDraftRawApplyResult>("clayDraftStore.applyRaw", cancellationToken, registrationId);
    }

    /// <summary>Применяет raw только в указанной области после её отдельной DOM/permission readiness.</summary>
    public async Task<ClayDraftRawApplyResult> ApplyRawWithResultAsync(
        string registrationId,
        string scope,
        CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        ValidateScope(scope);
        return await js.InvokeAsync<ClayDraftRawApplyResult>(
            "clayDraftStore.applyRaw", cancellationToken, registrationId, scope);
    }

    internal Task<ClayDraftRawApplyResult> ApplyPendingRawAsync(
        string registrationId, string scope, CancellationToken token, bool notifyInput = true)
    {
        RequireRegistration(registrationId);
        ValidateScope(scope);
        return js.InvokeAsync<ClayDraftRawApplyResult>(
            "clayDraftStore.applyPendingRaw", token, notifyInput ? [registrationId, scope] : [registrationId, scope, false]).AsTask();
    }

    internal Task<ClayDraftRawApplyResult> ApplyRawSilentlyAsync(string registrationId, string scope, CancellationToken token)
    {
        RequireRegistration(registrationId);
        return js.InvokeAsync<ClayDraftRawApplyResult>("clayDraftStore.applyRaw", token, registrationId, scope, false).AsTask();
    }

    /// <summary>Возвращает незавершённые и отклонённые области restore без raw значений или секретов.</summary>
    public async Task<ClayDraftRestoreReadiness> GetRestoreReadinessAsync(
        string registrationId,
        CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<ClayDraftRestoreReadiness>(
            "clayDraftStore.getRestoreReadiness", cancellationToken, registrationId);
    }

    /// <summary>Фиксирует failed readiness области; защищённый raw сохраняется для разрешённого retry.</summary>
    public async Task ReportRestoreScopeFailureAsync(
        string registrationId,
        string scope,
        CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        ValidateScope(scope);
        await js.InvokeVoidAsync("clayDraftStore.reportRestoreScopeFailure", cancellationToken, registrationId, scope);
    }

    /// <summary>Снимает tombstone отменённой области при явном новом открытии editor.</summary>
    public Task OpenScopeAsync(string registrationId, string scope, CancellationToken cancellationToken = default) =>
        InvokeScope(registrationId, "clayDraftStore.openScope", scope, cancellationToken);

    /// <summary>Очищает только cancelled scope и запрещает его поздний autosave.</summary>
    public Task DiscardScopeAsync(string registrationId, string scope, CancellationToken cancellationToken = default) =>
        InvokeScope(registrationId, "clayDraftStore.discardScope", scope, cancellationToken);

    /// <summary>Перечисляет сохранённые регистрации в том же удостоверенном контексте.</summary>
    internal async Task<IReadOnlyList<ClayDraftRecoveryCandidate>> ListRecoveryCandidatesAsync(string registrationId, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<ClayDraftRecoveryCandidate[]>("clayDraftStore.listRecoveryCandidates", cancellationToken, registrationId);
    }

    /// <summary>Восстанавливает явно выбранный отдельный черновик при доступном блокировании.</summary>
    internal async Task<ClayDraftRestore?> RecoverListedDraftAsync(string registrationId, string draftId, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        ClayDraftRestore? recovered;
        if (_registrations[registrationId].MaximumRestoreBytes is { } limit)
        {
            try
            {
                var envelope = await js.InvokeAsync<ClayDraftRestoreStream?>("clayDraftStore.recoverListedDraftStream",
                    cancellationToken, registrationId, draftId);
                recovered = envelope is null ? null : await ReadStreamedRestoreAsync(envelope, limit, cancellationToken);
                if (recovered is not null && recovered.RegistrationId != registrationId)
                    throw new InvalidOperationException("Изменился контекст восстановления черновика.");
            }
            catch
            {
                await DisposeRegistrationAsync(registrationId);
                throw;
            }
        }
        else
            recovered = await js.InvokeAsync<ClayDraftRestore?>("clayDraftStore.recoverListedDraft", cancellationToken, registrationId, draftId);
        if (recovered is not null && recovered.RegistrationId != registrationId)
            throw new InvalidOperationException("Изменился контекст восстановления черновика.");
        return recovered;
    }

    /// <summary>Читает состояние области перед явным решением об отмене, включая незавершённый ввод.</summary>
    internal async Task<ClayDraftEditorScopeState> ReadEditorScopeAsync(string registrationId, string scope, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<ClayDraftEditorScopeState>("clayDraftStore.readEditorScope", cancellationToken, registrationId, scope);
    }

    /// <summary>Читает состояние всех областей одного экземпляра без значений полей.</summary>
    internal async Task<ClayDraftEditorCloseState> ReadEditorCloseStateAsync(string registrationId, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<ClayDraftEditorCloseState>("clayDraftStore.readEditorCloseState", cancellationToken, registrationId);
    }

    /// <summary>Атомарно заменяет либо отзывает одну область при совпадении ревизии.</summary>
    internal async Task<bool> ReplaceEditorScopeAsync(string registrationId, string scope, long expectedRevision,
        string? replacement, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<bool>("clayDraftStore.replaceEditorScope", cancellationToken,
            registrationId, scope, expectedRevision, replacement);
    }

    internal async Task<bool> ReplaceNestedEditorScopeAsync(string registrationId, string scope, long expectedRevision,
        string parentState, bool close, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<bool>("clayDraftStore.replaceNestedEditorScope", cancellationToken,
            registrationId, scope, expectedRevision, parentState, close);
    }
    /// <summary>Читает версию и сырые ключи области перед явным выбором пользователя.</summary>
    public async Task<ClayDraftRebaseState> ReadRebaseStateAsync(string registrationId, string scope, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<ClayDraftRebaseState>("clayDraftStore.readRebaseState", cancellationToken, registrationId, scope);
    }

    /// <summary>Принимает выбранный снимок, если с начала сверки не появился новый ввод.</summary>
    public async Task<bool> RebaseScopeAsync(string registrationId, string scope, long expectedRevision, string snapshotJson,
        IReadOnlyList<string> discardedRaw, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<bool>("clayDraftStore.rebaseScope", cancellationToken,
            registrationId, scope, expectedRevision, snapshotJson, discardedRaw);
    }


    /// <summary>Возвращает health текущей регистрации.</summary>
    public Task<ClayDraftStorageHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        GetHealthAsync(RequireRegistration().RegistrationId, cancellationToken);

    /// <summary>Возвращает health указанной регистрации после ожидания pending writes.</summary>
    public async Task<ClayDraftStorageHealth> GetHealthAsync(string registrationId, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<ClayDraftStorageHealth>("clayDraftStore.getHealth", cancellationToken, registrationId);
    }

    /// <summary>Fail-closed gate текущей регистрации перед бизнес-командой.</summary>
    public Task EnsureCommandAllowedAsync(CancellationToken cancellationToken = default) =>
        EnsureCommandAllowedAsync(RequireRegistration().RegistrationId, cancellationToken);

    /// <summary>Запрещает команду при ошибке хранилища или незавершённой записи черновика.</summary>
    public async Task EnsureCommandAllowedAsync(string registrationId, CancellationToken cancellationToken = default)
    {
        var health = await GetHealthAsync(registrationId, cancellationToken);
        if (!health.Healthy)
            throw new InvalidOperationException(health.LastError ??
                "Черновик ещё не сохранён. Бизнес-команда заблокирована до подтверждения хранилища.");
    }

    /// <summary>Явно отбрасывает текущий draft и запрещает поздний autosave.</summary>
    public Task DiscardAsync(CancellationToken cancellationToken = default) =>
        DiscardAsync(RequireRegistration().RegistrationId, cancellationToken);

    /// <summary>Отбрасывает editable draft; running/unknown operation сохраняется до доказанного outcome.</summary>
    public async Task DiscardAsync(string registrationId, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        await js.InvokeVoidAsync("clayDraftStore.discard", cancellationToken, registrationId);
        RemoveRegistration(registrationId);
    }

    internal async Task CloseRamAsync(string registrationId, CancellationToken cancellationToken)
    {
        if (!RequireRegistration(registrationId).IsRam)
            throw new InvalidOperationException("Закрытие RAM-черновика требует регистрации нового формата.");

        await js.InvokeVoidAsync("clayDraftStore.closeRam", cancellationToken, registrationId);
        RemoveRegistration(registrationId);
    }

    internal async Task<bool> CloseRamIfRevisionAsync(string registrationId, long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (!RequireRegistration(registrationId).IsRam)
            throw new InvalidOperationException("Закрытие RAM-черновика требует регистрации нового формата.");

        var closed = await js.InvokeAsync<bool>("clayDraftStore.closeRam", cancellationToken,
            registrationId, expectedRevision);
        if (closed) RemoveRegistration(registrationId);
        return closed;
    }

    /// <summary>Снимает текущую live-регистрацию без удаления durable record.</summary>
    public Task ReleaseAsync(CancellationToken cancellationToken = default) =>
        ReleaseAsync(RequireRegistration().RegistrationId, cancellationToken);

    /// <summary>Снимает listeners/lock указанной регистрации без удаления durable record.</summary>
    public async Task ReleaseAsync(string registrationId, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        try
        {
            await js.InvokeVoidAsync("clayDraftStore.unregister", cancellationToken, registrationId);
        }
        finally
        {
            RemoveRegistration(registrationId);
        }
    }

    private async Task InvokeOperation(string registrationId, string method, long revision, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        await js.InvokeVoidAsync(method, cancellationToken, registrationId, revision);
    }

    private async Task InvokeScope(string registrationId, string method, string scope, CancellationToken cancellationToken)
    {
        RequireRegistration(registrationId);
        ValidateScope(scope);
        await js.InvokeVoidAsync(method, cancellationToken, registrationId, scope);
    }

    private static void ValidateScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Draft scope is required.", nameof(scope));
    }

    private ClayDraftRegistration RequireRegistration() =>
        _registrationId is not null && _registrations.TryGetValue(_registrationId, out var registration)
            ? registration
            : throw new InvalidOperationException("Черновик ещё не зарегистрирован; бизнес-команда заблокирована.");

    private ClayDraftRegistration RequireRegistration(string registrationId) =>
        _registrations.TryGetValue(registrationId, out var registration)
            ? registration
            : throw new InvalidOperationException("Draft registration is not active.");

    private void RemoveRegistration(string registrationId)
    {
        _registrations.Remove(registrationId);
        if (_registrationId == registrationId)
            _registrationId = null;
    }

    internal async ValueTask DisposeRegistrationAsync(string registrationId)
    {
        if (!_registrations.ContainsKey(registrationId))
            return;
        try
        {
            await js.InvokeVoidAsync("clayDraftStore.unregister", registrationId);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException)
        {
        }
        finally
        {
            RemoveRegistration(registrationId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var registrationId in _registrations.Keys.ToArray())
            await DisposeRegistrationAsync(registrationId);
        _registrationId = null;
    }
}
