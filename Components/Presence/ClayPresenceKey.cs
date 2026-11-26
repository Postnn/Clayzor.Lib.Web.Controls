using System.Security.Claims;
using System.Text.Json;

namespace Clayzor.Lib.Web.Controls.Components.Presence;

/// <summary>Каноническая идентичность записи в доверенной области данных.</summary>
public sealed record ClayPresenceKey
{
    private ClayPresenceKey(string dataScope, string entityType, string serializedKey)
    {
        DataScope = dataScope;
        EntityType = entityType;
        SerializedKey = serializedKey;
    }

    /// <summary>Каноническая область данных, определённая приложением на сервере.</summary>
    public string DataScope { get; }

    /// <summary>Согласованное имя доменного типа, общее для его маршрутов и приложений.</summary>
    public string EntityType { get; }

    /// <summary>Последовательность типизированных частей ключа в доменном порядке.</summary>
    public string SerializedKey { get; }

    /// <summary>Создаёт ключ из уже нормализованных серверным адаптером частей.</summary>
    public static ClayPresenceKey Create(string dataScope, string entityType, params ClayPresenceKeyPart[] parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0 || parts.Any(part => part.Value is null))
            throw new ArgumentException("Требуется полный ключ существующей записи.", nameof(parts));

        return new(dataScope, entityType, JsonSerializer.Serialize(parts.Select(part => part.Value)));
    }
}

/// <summary>Числовая или текстовая часть канонического доменного ключа.</summary>
public readonly struct ClayPresenceKeyPart
{
    private ClayPresenceKeyPart(object value) => Value = value;

    internal object? Value { get; }

    /// <summary>Преобразует положительный идентификатор существующей записи в часть ключа.</summary>
    public static implicit operator ClayPresenceKeyPart(long value) =>
        value > 0 ? new(value) : throw new ArgumentOutOfRangeException(nameof(value));

    /// <summary>Преобразует канонический текстовый идентификатор в часть ключа.</summary>
    public static implicit operator ClayPresenceKeyPart(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl) || value.Any(char.IsSurrogate) && !IsWellFormed(value))
            throw new ArgumentException("Некорректная текстовая часть ключа.", nameof(value));

        return new(value);
    }

    private static bool IsWellFormed(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
                continue;

            if (!char.IsSurrogatePair(value, index))
                return false;

            index++;
        }

        return true;
    }
}

/// <summary>Состояние работы сотрудника с записью.</summary>
public enum ClayPresenceActivity
{
    /// <summary>Читает существующую запись.</summary>
    Viewing,
    /// <summary>Работает в редакторе с правом редактирования.</summary>
    Editing
}

/// <summary>Минимальные сведения о сотруднике, подтверждённые серверным адаптером.</summary>
/// <param name="EmployeeId">Стабильный идентификатор сотрудника в области данных.</param>
/// <param name="DisplayName">Разрешённое для отображения имя.</param>
/// <param name="Activity">Подтверждённое состояние работы.</param>
public sealed record ClayPresenceAccess(int EmployeeId, string DisplayName, ClayPresenceActivity Activity);

/// <summary>Ключ формы и её серверная проверка доступа, выполняемая при каждом heartbeat.</summary>
/// <param name="Key">Ключ существующей записи из успешного серверного чтения.</param>
/// <param name="AuthorizeAsync">Проверяет текущего сотрудника и целевую запись, возвращает null при отказе.</param>
public sealed record ClayPresenceTarget(
    ClayPresenceKey Key,
    Func<ClaimsPrincipal, CancellationToken, ValueTask<ClayPresenceAccess?>> AuthorizeAsync);
