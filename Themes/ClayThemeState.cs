using Microsoft.Extensions.Logging;

namespace Clayzor.Lib.Web.Controls.Themes;

/// <summary>Постоянное хранилище пользовательского выбора темы.</summary>
public interface IClayThemePreferenceStore
{
    /// <summary>Загружает сериализованное сочетание темы и режима.</summary>
    /// <param name="cancellationToken">Токен отмены загрузки.</param>
    Task<string?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Сохраняет сериализованное сочетание темы и режима.</summary>
    /// <param name="value">Значение настройки.</param>
    /// <param name="cancellationToken">Токен отмены сохранения.</param>
    Task SaveAsync(string value, CancellationToken cancellationToken);
}

/// <summary>Применяет выбор сразу и упорядочивает сохранение внутри одного circuit.</summary>
public sealed class ClayThemeState(
    ILogger<ClayThemeState> logger,
    IClayThemePreferenceStore? store = null) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _saveGate = new(1);
    private Task? _initialization;
    private ClayThemePreference? _saved;
    private long _revision;
    private bool _disposed;

    /// <summary>Текущее сочетание темы и режима в этом circuit.</summary>
    public ClayThemePreference Preference { get; private set; }

    /// <summary>Признак ошибки загрузки сохранённой темы.</summary>
    public bool LoadFailed { get; private set; }

    /// <summary>Признак ошибки сохранения выбранной темы.</summary>
    public bool SaveFailed { get; private set; }

    /// <summary>Событие изменения темы или состояния её загрузки и сохранения.</summary>
    public event Action? Changed;

    /// <summary>Однократно загружает сохранённую тему для текущего circuit.</summary>
    public Task InitializeAsync() => _initialization ??= LoadAsync();

    /// <summary>Применяет выбранную тему и сохраняет её без отката более позднего выбора.</summary>
    /// <param name="preference">Новое сочетание темы и режима.</param>
    public async Task SelectAsync(ClayThemePreference preference)
    {
        if (_disposed || preference == Preference && !SaveFailed && !LoadFailed &&
            (store is null || _revision != 0 || _initialization?.IsCompleted == true))
            return;

        Preference = preference;
        SaveFailed = false;
        var revision = ++_revision;
        Changed?.Invoke();

        if (store is null)
            return;

        var cancellationToken = _lifetime.Token;
        try
        {
            await _saveGate.WaitAsync(cancellationToken);
            try
            {
                if (revision != _revision || preference == _saved)
                    return;

                try
                {
                    await store.SaveAsync(preference.Serialize(), cancellationToken);
                    _saved = preference;
                    LoadFailed = false;
                    Changed?.Invoke();
                }
                catch
                {
                    _saved = null;
                    throw;
                }
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not save the Clayzor theme preference.");
            if (revision == _revision && !_disposed)
            {
                SaveFailed = true;
                Changed?.Invoke();
            }
        }
    }

    private async Task LoadAsync()
    {
        if (store is null || _disposed || _revision != 0)
            return;

        var cancellationToken = _lifetime.Token;
        try
        {
            var stored = await store.LoadAsync(cancellationToken);
            if (_revision != 0 || _disposed)
                return;

            Preference = ClayThemeCatalog.Parse(stored);
            if (stored == Preference.Serialize())
                _saved = Preference;
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not load the Clayzor theme preference.");
            if (_revision == 0 && !_disposed)
            {
                LoadFailed = true;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Отменяет незавершённые операции и освобождает ресурсы состояния.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
