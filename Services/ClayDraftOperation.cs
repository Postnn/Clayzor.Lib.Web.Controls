using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Состояние durable-попытки, при котором повтор бизнес-команды запрещён до сверки.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClayDraftOperationStatus>))]
public enum ClayDraftOperationStatus
{
    /// <summary>Команда подготовлена или отправлена, подтверждённого результата ещё нет.</summary>
    Running,

    /// <summary>Ответ потерян после возможного эффекта; требуется read-only сверка.</summary>
    Unknown,
}

/// <summary>Типизированное намерение durable-попытки с точными областями, которые может принять commit.</summary>
public sealed record ClayDraftOperationIntent(
    string Kind,
    string Data,
    IReadOnlyList<string>? AcceptedScopes = null,
    string? AttemptId = null,
    bool AcceptLegacyTyped = false);

/// <summary>Ревизии typed/raw состояния одной области на момент BeginOperation.</summary>
public sealed record ClayDraftOperationScope(
    string Name,
    long TypedRevision,
    IReadOnlyDictionary<string, long> RawRevisions);

/// <summary>Версионированный snapshot команды, сохраняемый до отправки и переживающий reconnect.</summary>
public sealed record ClayDraftOperation(
    int Version,
    string AttemptId,
    string Kind,
    ClayDraftOperationStatus Status,
    long Revision,
    string Data,
    IReadOnlyList<ClayDraftOperationScope> Scopes,
    bool AcceptLegacyTyped,
    long LegacyTypedRevision)
{
    /// <summary>Текущая версия wire-формата operation snapshot.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>Строгий production-сериализатор versioned JSON протокола операций.</summary>
public static class ClayDraftOperationSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Сериализует намерение для передачи в clayDraftStore.beginOperationIntent.</summary>
    public static string SerializeIntent(ClayDraftOperationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.Kind))
            throw new ArgumentException("Operation kind is required.", nameof(intent));
        if (intent.Data is null)
            throw new ArgumentException("Operation data is required.", nameof(intent));
        if (intent.AcceptedScopes?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Accepted scope names must not be empty.", nameof(intent));
        if (intent.AcceptedScopes is not null &&
            intent.AcceptedScopes.Distinct(StringComparer.Ordinal).Count() != intent.AcceptedScopes.Count)
            throw new ArgumentException("Accepted scope names must be unique.", nameof(intent));

        return JsonSerializer.Serialize(new
        {
            version = ClayDraftOperation.CurrentVersion,
            kind = intent.Kind,
            data = intent.Data,
            acceptedScopes = intent.AcceptedScopes ?? [],
            attemptId = intent.AttemptId,
            acceptLegacyTyped = intent.AcceptLegacyTyped,
        }, Options);
    }

    /// <summary>Десериализует и проверяет JSON, полученный непосредственно из production JS.</summary>
    public static ClayDraftOperation Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Operation snapshot is empty.");

        var operation = JsonSerializer.Deserialize<ClayDraftOperation>(json, Options)
                        ?? throw new InvalidOperationException("Operation snapshot is empty.");
        if (operation.Version != ClayDraftOperation.CurrentVersion)
            throw new InvalidOperationException("Operation snapshot has an unsupported version.");
        if (string.IsNullOrWhiteSpace(operation.AttemptId) || string.IsNullOrWhiteSpace(operation.Kind))
            throw new InvalidOperationException("Operation snapshot has no attempt identity.");
        if (operation.Data is null || operation.Revision <= 0 ||
            !Enum.IsDefined(operation.Status) || operation.Scopes is null)
            throw new InvalidOperationException("Operation snapshot is incomplete.");
        if (operation.Scopes.Any(scope => string.IsNullOrWhiteSpace(scope.Name) || scope.RawRevisions is null) ||
            operation.Scopes.Select(scope => scope.Name).Distinct(StringComparer.Ordinal).Count() != operation.Scopes.Count)
            throw new InvalidOperationException("Operation snapshot has invalid accepted scopes.");

        return operation;
    }
}
