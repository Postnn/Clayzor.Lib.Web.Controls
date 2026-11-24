using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Восстановленное состояние черновика конкретной регистрации.</summary>
public sealed record ClayDraftRestore(
    [property: JsonPropertyName("registrationId")] string RegistrationId,
    [property: JsonPropertyName("hasDraft")] bool HasDraft,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("typedState")] string? TypedState,
    [property: JsonPropertyName("operation")] string? Operation,
    [property: JsonPropertyName("typedScopes")] IReadOnlyDictionary<string, string>? TypedScopes = null)
{
    /// <summary>Строго десериализованный versioned running/unknown marker.</summary>
    [JsonIgnore]
    public ClayDraftOperation? OperationSnapshot => Operation is null
        ? null
        : ClayDraftOperationSerializer.Deserialize(Operation);
}

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
    [property: JsonPropertyName("hasOperation")] bool HasOperation,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("persistedRevision")] long PersistedRevision);

/// <summary>Общий DR-01 API долговечного клиентского черновика и snapshot бизнес-операции.</summary>
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

    private async Task<ClayDraftRegistration> RegisterCoreAsync(
        string formKey,
        string entityKey,
        string userKey,
        int schemaVersion,
        string? rootSelector,
        CancellationToken cancellationToken)
    {
        if (rootSelector is not null && string.IsNullOrWhiteSpace(rootSelector))
            throw new ArgumentException("Draft DOM root selector is required.", nameof(rootSelector));
        var restore = rootSelector is null
            ? await js.InvokeAsync<ClayDraftRestore?>(
                "clayDraftStore.register", cancellationToken, formKey, entityKey, userKey, schemaVersion)
            : await js.InvokeAsync<ClayDraftRestore?>(
                "clayDraftStore.register", cancellationToken, formKey, entityKey, userKey, schemaVersion, rootSelector);
        if (restore is null || string.IsNullOrWhiteSpace(restore.RegistrationId))
            throw new InvalidOperationException("Draft store did not confirm registration; commands remain blocked.");
        if (_registrations.ContainsKey(restore.RegistrationId))
            throw new InvalidOperationException("Draft store returned a duplicate registration ID.");
        try
        {
            if (restore.Operation is not null)
                _ = restore.OperationSnapshot;
            var registration = new ClayDraftRegistration(this, restore);
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

    /// <summary>Фиксирует legacy snapshot без автоматического принятия main/child областей.</summary>
    public Task<long> BeginOperationAsync(string operation, CancellationToken cancellationToken = default) =>
        BeginOperationAsync(RequireRegistration().RegistrationId, operation, cancellationToken);

    /// <summary>Фиксирует legacy snapshot указанной регистрации.</summary>
    public async Task<long> BeginOperationAsync(string registrationId, string operation, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        return await js.InvokeAsync<long>("clayDraftStore.beginOperation", cancellationToken, registrationId, operation);
    }

    /// <summary>Фиксирует versioned intent текущей регистрации и возвращает durable marker.</summary>
    public Task<ClayDraftOperation> BeginOperationAsync(ClayDraftOperationIntent intent, CancellationToken cancellationToken = default) =>
        BeginOperationAsync(RequireRegistration().RegistrationId, intent, cancellationToken);

    /// <summary>Фиксирует versioned intent указанной регистрации через настоящий JSON wire contract.</summary>
    public async Task<ClayDraftOperation> BeginOperationAsync(
        string registrationId,
        ClayDraftOperationIntent intent,
        CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        var intentJson = ClayDraftOperationSerializer.SerializeIntent(intent);
        var operationJson = await js.InvokeAsync<string>(
            "clayDraftStore.beginOperationIntent", cancellationToken, registrationId, intentJson);
        return ClayDraftOperationSerializer.Deserialize(operationJson);
    }

    /// <summary>Снимает running snapshot после доказанного отказа до записи.</summary>
    public Task RejectOperationAsync(long revision, CancellationToken cancellationToken = default) =>
        RejectOperationAsync(RequireRegistration().RegistrationId, revision, cancellationToken);

    /// <summary>Снимает running snapshot указанной регистрации.</summary>
    public Task RejectOperationAsync(string registrationId, long revision, CancellationToken cancellationToken = default) =>
        InvokeOperation(registrationId, "clayDraftStore.rejectOperation", revision, cancellationToken);

    /// <summary>Сохраняет Unknown для последующей сверки без разрешения replay.</summary>
    public Task UnknownOperationAsync(long revision, CancellationToken cancellationToken = default) =>
        UnknownOperationAsync(RequireRegistration().RegistrationId, revision, cancellationToken);

    /// <summary>Сохраняет Unknown указанной регистрации.</summary>
    public Task UnknownOperationAsync(string registrationId, long revision, CancellationToken cancellationToken = default) =>
        InvokeOperation(registrationId, "clayDraftStore.unknownOperation", revision, cancellationToken);

    /// <summary>Принимает только scope/revision из Begin и сохраняет late/unrelated input.</summary>
    public Task CommitOperationAsync(long revision, string? cleanTypedState = null, CancellationToken cancellationToken = default) =>
        CommitOperationAsync(RequireRegistration().RegistrationId, revision, cleanTypedState, cancellationToken);

    /// <summary>Принимает только scope/revision из Begin указанной регистрации.</summary>
    public async Task CommitOperationAsync(string registrationId, long revision, string? cleanTypedState = null, CancellationToken cancellationToken = default)
    {
        RequireRegistration(registrationId);
        await js.InvokeVoidAsync("clayDraftStore.commitOperation", cancellationToken, registrationId, revision, cleanTypedState);
    }

    /// <summary>Снимает tombstone отменённой области при явном новом открытии editor.</summary>
    public Task OpenScopeAsync(string registrationId, string scope, CancellationToken cancellationToken = default) =>
        InvokeScope(registrationId, "clayDraftStore.openScope", scope, cancellationToken);

    /// <summary>Очищает только cancelled scope и запрещает его поздний autosave.</summary>
    public Task DiscardScopeAsync(string registrationId, string scope, CancellationToken cancellationToken = default) =>
        InvokeScope(registrationId, "clayDraftStore.discardScope", scope, cancellationToken);

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

    /// <summary>Запрещает команду при storage failure, pending writes или running/unknown marker.</summary>
    public async Task EnsureCommandAllowedAsync(string registrationId, CancellationToken cancellationToken = default)
    {
        var health = await GetHealthAsync(registrationId, cancellationToken);
        if (!health.Healthy)
            throw new InvalidOperationException(health.LastError ??
                "Черновик ещё не сохранён. Бизнес-команда заблокирована до подтверждения хранилища.");
        if (health.HasOperation)
            throw new InvalidOperationException("Предыдущая команда не сверена. Повторная отправка заблокирована.");
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
