namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Независимый handle регистрации формы/сущности/editor-session в общем draft store.</summary>
public sealed class ClayDraftRegistration : IAsyncDisposable
{
    private readonly ClayDraftService _owner;

    internal ClayDraftRegistration(ClayDraftService owner, ClayDraftRestore restore)
    {
        _owner = owner;
        Restore = restore;
    }

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

    /// <summary>Возвращает pending/failed области без значений полей; Complete=false запрещает объявлять restore завершённым.</summary>
    public Task<ClayDraftRestoreReadiness> GetRestoreReadinessAsync(CancellationToken cancellationToken = default) =>
        _owner.GetRestoreReadinessAsync(RegistrationId, cancellationToken);

    /// <summary>Сохраняет failed readiness области и защищённый raw для последующего разрешённого retry.</summary>
    public Task ReportRestoreScopeFailureAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ReportRestoreScopeFailureAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Сохраняет legacy running marker; не принимает автоматически ни одну область при commit.</summary>
    public Task<long> BeginOperationAsync(string operation, CancellationToken cancellationToken = default) =>
        _owner.BeginOperationAsync(RegistrationId, operation, cancellationToken);

    /// <summary>Сохраняет типизированную попытку с accepted scopes и стабильным attempt ID.</summary>
    public Task<ClayDraftOperation> BeginOperationAsync(ClayDraftOperationIntent intent, CancellationToken cancellationToken = default) =>
        _owner.BeginOperationAsync(RegistrationId, intent, cancellationToken);

    /// <summary>Завершает попытку как доказанно не отправленную/отклонённую.</summary>
    public Task RejectOperationAsync(long revision, CancellationToken cancellationToken = default) =>
        _owner.RejectOperationAsync(RegistrationId, revision, cancellationToken);

    /// <summary>Сохраняет Unknown до предметной read-only сверки.</summary>
    public Task UnknownOperationAsync(long revision, CancellationToken cancellationToken = default) =>
        _owner.UnknownOperationAsync(RegistrationId, revision, cancellationToken);

    /// <summary>Фиксирует подтверждённый результат операции перед локальной очисткой этой же попытки.</summary>
    public Task RecordConfirmedOperationAsync(long revision, CancellationToken cancellationToken = default) =>
        _owner.RecordConfirmedOperationAsync(RegistrationId, revision, cancellationToken);

    /// <summary>Принимает только scopes/revisions, зафиксированные в BeginOperation.</summary>
    public Task CommitOperationAsync(long revision, string? cleanTypedState = null, CancellationToken cancellationToken = default) =>
        _owner.CommitOperationAsync(RegistrationId, revision, cleanTypedState, cancellationToken);

    /// <summary>Открывает область после явного cancel; обычный reopen выполняет владелец editor.</summary>
    public Task OpenScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.OpenScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Удаляет raw/typed только отменённой области и блокирует её поздний autosave.</summary>
    public Task DiscardScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.DiscardScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Повторяет только запись черновика, сохраняя маркер любого неподтверждённого исхода.</summary>
    public Task<ClayDraftOperation?> RetryStorageAsync(string? notSentAttemptId, CancellationToken cancellationToken = default) =>
        _owner.RetryStorageAsync(RegistrationId, notSentAttemptId, cancellationToken);


    /// <summary>Получает ревизию области и признаки raw/операции перед вопросом об отмене.</summary>
    public Task<ClayDraftEditorScopeState> ReadEditorScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.ReadEditorScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Атомарно принимает свежий снимок либо отменяет область, если ревизия не изменилась.</summary>
    public Task<bool> ReplaceEditorScopeAsync(string scope, long expectedRevision, string? replacement,
        CancellationToken cancellationToken = default) =>
        _owner.ReplaceEditorScopeAsync(RegistrationId, scope, expectedRevision, replacement, cancellationToken);

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

    /// <summary>Удаляет editable values и backup; running/unknown marker остаётся durable до доказанного outcome.</summary>
    public Task DiscardAsync(CancellationToken cancellationToken = default) =>
        _owner.DiscardAsync(RegistrationId, cancellationToken);

    /// <summary>Снимает listeners/lock, сохраняя durable draft для повторного открытия.</summary>
    public Task ReleaseAsync(CancellationToken cancellationToken = default) =>
        _owner.ReleaseAsync(RegistrationId, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _owner.DisposeRegistrationAsync(RegistrationId);
}
