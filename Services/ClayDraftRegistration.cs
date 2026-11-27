namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Независимый handle регистрации формы/сущности/editor-session в общем draft store.</summary>
public sealed class ClayDraftRegistration : IAsyncDisposable
{
    private readonly ClayDraftService _owner;

    internal ClayDraftRegistration(ClayDraftService owner, ClayDraftRestore restore, long? maximumRestoreBytes = null,
        bool isRam = false)
    {
        _owner = owner;
        Restore = restore;
        MaximumRestoreBytes = maximumRestoreBytes;
        IsRam = isRam;
    }

    internal long? MaximumRestoreBytes { get; }
    internal bool IsRam { get; }

    /// <summary>Состояние, восстановленное при регистрации.</summary>
    public ClayDraftRestore Restore { get; private set; }

    /// <summary>Устойчивый идентификатор именно этой live-регистрации.</summary>
    public string RegistrationId => Restore.RegistrationId;

    /// <summary>Читает метаданные собственных черновиков этого контекста.</summary>
    public Task<IReadOnlyList<ClayDraftRecoveryCandidate>> ListRecoveryCandidatesAsync(CancellationToken cancellationToken = default) =>
        _owner.ListRecoveryCandidatesAsync(RegistrationId, cancellationToken);

    /// <summary>Заменяет новую пустую регистрацию выбранным черновиком, не смешивая ввод.</summary>
    public async Task<bool> RecoverListedDraftAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var recovered = await _owner.RecoverListedDraftAsync(RegistrationId, draftId, cancellationToken);
        if (recovered is null) return false;
        Restore = recovered;
        return true;
    }

    /// <summary>Сохраняет legacy typed snapshot целиком без секретов.</summary>
    public Task<long> SaveTypedAsync(string typedState, CancellationToken cancellationToken = default) =>
        _owner.SaveTypedAsync(RegistrationId, typedState, cancellationToken);

    /// <summary>Сохраняет typed snapshot отдельной области, не изменяя остальные области.</summary>
    public Task<long> SaveTypedScopeAsync(string scope, string typedState, CancellationToken cancellationToken = default) =>
        _owner.SaveTypedScopeAsync(RegistrationId, scope, typedState, cancellationToken);

    /// <summary>Ожидает применение raw ввода; ошибки restore наблюдаются вызывающим кодом.</summary>
    public Task ApplyRawAsync(CancellationToken cancellationToken = default) =>
        _owner.ApplyRawAsync(RegistrationId, cancellationToken);

    /// <summary>Ожидает raw restore и возвращает применённые и ожидающие readiness ключи.</summary>
    public Task<ClayDraftRawApplyResult> ApplyRawWithResultAsync(CancellationToken cancellationToken = default) =>
        _owner.ApplyRawWithResultAsync(RegistrationId, cancellationToken);

    /// <summary>Применяет raw только этой editor scope после отдельной permission/DOM readiness.</summary>
    public Task<ClayDraftRawApplyResult> ApplyRawWithResultAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ApplyRawWithResultAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Восстанавливает только ожидающие значения одной области, сохраняя уже восстановленный и новый ввод.</summary>
    public Task<ClayDraftRawApplyResult> ApplyPendingRawAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ApplyPendingRawAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Восстанавливает ожидающие значения одной области без событий ввода.</summary>
    public Task<ClayDraftRawApplyResult> ApplyPendingRawSilentlyAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ApplyPendingRawAsync(RegistrationId, scope, cancellationToken, notifyInput: false);

    /// <summary>Восстанавливает DOM без событий ввода; вызывающая форма самостоятельно перечитывает значения.</summary>
    public Task<ClayDraftRawApplyResult> ApplyRawSilentlyAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ApplyRawSilentlyAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Возвращает pending/failed области без значений полей; Complete=false запрещает объявлять restore завершённым.</summary>
    public Task<ClayDraftRestoreReadiness> GetRestoreReadinessAsync(CancellationToken cancellationToken = default) =>
        _owner.GetRestoreReadinessAsync(RegistrationId, cancellationToken);

    /// <summary>Сохраняет failed readiness области и защищённый raw для последующего разрешённого retry.</summary>
    public Task ReportRestoreScopeFailureAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ReportRestoreScopeFailureAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Открывает область после явного cancel; обычный reopen выполняет владелец editor.</summary>
    public Task OpenScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.OpenScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Удаляет raw/typed только отменённой области и блокирует её поздний autosave.</summary>
    public Task DiscardScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.DiscardScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Получает ревизию области и признаки raw/операции перед вопросом об отмене.</summary>
    public Task<ClayDraftEditorScopeState> ReadEditorScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ReadEditorScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Получает ревизию и признаки ввода во всех областях экземпляра перед закрытием.</summary>
    public Task<ClayDraftEditorCloseState> ReadEditorCloseStateAsync(CancellationToken cancellationToken = default) =>
        _owner.ReadEditorCloseStateAsync(RegistrationId, cancellationToken);

    /// <summary>Атомарно принимает свежий снимок либо отменяет область, если ревизия не изменилась.</summary>
    public Task<bool> ReplaceEditorScopeAsync(string scope, long expectedRevision, string? replacement,
        CancellationToken cancellationToken = default) =>
        _owner.ReplaceEditorScopeAsync(RegistrationId, scope, expectedRevision, replacement, cancellationToken);

    /// <summary>Принимает новую ревизию снимка владельца и удаляет ввод только указанного вложенного редактора.</summary>
    public Task<bool> ReplaceNestedEditorScopeAsync(string scope, long expectedRevision, string parentState, bool close,
        CancellationToken cancellationToken = default) =>
        _owner.ReplaceNestedEditorScopeAsync(RegistrationId, scope, expectedRevision, parentState, close, cancellationToken);

    /// <summary>Читает версию и сырые ключи области перед явным разрешением конфликта.</summary>
    public Task<ClayDraftRebaseState> ReadRebaseStateAsync(string scope, CancellationToken cancellationToken) =>
        _owner.ReadRebaseStateAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Сохраняет выбранную пользователем baseline и значения области.</summary>
    public Task<bool> RebaseScopeAsync(string scope, long expectedRevision, string snapshotJson, IReadOnlyList<string> discardedRaw,
        CancellationToken cancellationToken) =>
        _owner.RebaseScopeAsync(RegistrationId, scope, expectedRevision, snapshotJson, discardedRaw, cancellationToken);


    /// <summary>Возвращает health хранилища, pending writes и наличие unresolved marker.</summary>
    public Task<ClayDraftStorageHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        _owner.GetHealthAsync(RegistrationId, cancellationToken);

    /// <summary>Fail-closed gate перед командой: storage здоров и нет running/unknown попытки.</summary>
    public Task EnsureCommandAllowedAsync(CancellationToken cancellationToken = default) =>
        _owner.EnsureCommandAllowedAsync(RegistrationId, cancellationToken);

    /// <summary>Проверяет хранилище и восстановление именно тех областей, которые собирается принять команда.</summary>
    public async Task EnsureScopesCanBeAcceptedAsync(IReadOnlyCollection<string> acceptedScopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acceptedScopes);
        await EnsureCommandAllowedAsync(cancellationToken);
        var readiness = await GetRestoreReadinessAsync(cancellationToken);
        if (readiness.PendingScopes.Any(acceptedScopes.Contains) || readiness.FailedScopes.Any(acceptedScopes.Contains))
            throw new InvalidOperationException("Сначала полностью восстановите ввод в принимаемых областях.");
    }

    /// <summary>Удаляет значения полей и резервную копию черновика.</summary>
    public Task DiscardAsync(CancellationToken cancellationToken = default) =>
        _owner.DiscardAsync(RegistrationId, cancellationToken);

    /// <summary>Штатно закрывает RAM-экземпляр после подтверждённой физической очистки клиентского черновика.</summary>
    public Task CloseRamAsync(CancellationToken cancellationToken = default) =>
        _owner.CloseRamAsync(RegistrationId, cancellationToken);

    /// <summary>Закрывает RAM-экземпляр только при совпадении принятой ревизии.</summary>
    public Task<bool> CloseRamIfRevisionAsync(long expectedRevision, CancellationToken cancellationToken = default) =>
        _owner.CloseRamIfRevisionAsync(RegistrationId, expectedRevision, cancellationToken);

    /// <summary>Снимает listeners/lock, сохраняя durable draft для повторного открытия.</summary>
    public Task ReleaseAsync(CancellationToken cancellationToken = default) =>
        _owner.ReleaseAsync(RegistrationId, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _owner.DisposeRegistrationAsync(RegistrationId);
}
