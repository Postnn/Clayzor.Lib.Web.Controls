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
    public ClayDraftRestore Restore { get; }

    /// <summary>Устойчивый идентификатор именно этой live-регистрации.</summary>
    public string RegistrationId => Restore.RegistrationId;

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

    /// <summary>Принимает только scopes/revisions, зафиксированные в BeginOperation.</summary>
    public Task CommitOperationAsync(long revision, string? cleanTypedState = null, CancellationToken cancellationToken = default) =>
        _owner.CommitOperationAsync(RegistrationId, revision, cleanTypedState, cancellationToken);

    /// <summary>Открывает область после явного cancel; обычный reopen выполняет владелец editor.</summary>
    public Task OpenScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.OpenScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Удаляет raw/typed только отменённой области и блокирует её поздний autosave.</summary>
    public Task DiscardScopeAsync(string scope, CancellationToken cancellationToken = default) =>
        _owner.DiscardScopeAsync(RegistrationId, scope, cancellationToken);

    /// <summary>Возвращает health хранилища, pending writes и наличие unresolved marker.</summary>
    public Task<ClayDraftStorageHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        _owner.GetHealthAsync(RegistrationId, cancellationToken);

    /// <summary>Fail-closed gate перед командой: storage здоров и нет running/unknown попытки.</summary>
    public Task EnsureCommandAllowedAsync(CancellationToken cancellationToken = default) =>
        _owner.EnsureCommandAllowedAsync(RegistrationId, cancellationToken);

    /// <summary>Явно удаляет весь draft и backup этой регистрации.</summary>
    public Task DiscardAsync(CancellationToken cancellationToken = default) =>
        _owner.DiscardAsync(RegistrationId, cancellationToken);

    /// <summary>Снимает listeners/lock, сохраняя durable draft для повторного открытия.</summary>
    public Task ReleaseAsync(CancellationToken cancellationToken = default) =>
        _owner.ReleaseAsync(RegistrationId, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _owner.DisposeRegistrationAsync(RegistrationId);
}
