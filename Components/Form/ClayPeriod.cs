namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Календарный интервал для фильтра отчёта.</summary>
public enum ClayPeriodKind { Day = 1, Week, Month, Quarter, Year, Custom }

/// <summary>Управляемый выбор типа и границ календарного интервала.</summary>
public sealed record ClayPeriod(ClayPeriodKind Kind, DateTime? From, DateTime? To)
{
    /// <summary>Выбирает тип по legacy-правилу опорной даты.</summary>
    public ClayPeriod Select(ClayPeriodKind kind, DateTime today) => kind switch
    {
        ClayPeriodKind.Day => At(kind, today),
        ClayPeriodKind.Custom => this with { Kind = kind },
        _ => At(kind, kind > Kind ? From ?? today : today)
    };

    /// <summary>Переносит на соседний календарный период.</summary>
    public ClayPeriod Move(int delta)
    {
        if (From is not { } from || Kind == ClayPeriodKind.Custom) return this;
        try
        {
            return At(Kind, Kind switch
            {
                ClayPeriodKind.Day => from.AddDays(delta),
                ClayPeriodKind.Week => from.AddDays(delta * 7),
                ClayPeriodKind.Month => from.AddMonths(delta),
                ClayPeriodKind.Quarter => from.AddMonths(delta * 3),
                ClayPeriodKind.Year => from.AddYears(delta),
                _ => from
            });
        }
        catch (ArgumentOutOfRangeException) { return this; }
    }

    /// <summary>Сохраняет дату начала и согласует окончание.</summary>
    public ClayPeriod Start(DateTime? value) => Kind == ClayPeriodKind.Day
        ? new(Kind, value?.Date, value?.Date)
        : new(ClayPeriodKind.Custom, value?.Date, value > To ? value?.Date : To);

    /// <summary>Сохраняет дату окончания и согласует начало.</summary>
    public ClayPeriod End(DateTime? value) => Kind == ClayPeriodKind.Day ? Start(value)
        : new(ClayPeriodKind.Custom, value < From ? value?.Date : From, value?.Date);

    /// <summary>Проверяет полноту границ, включающих обе даты.</summary>
    public bool IsComplete => Enum.IsDefined(Kind) && From is not null && To is not null && From <= To;

    private static ClayPeriod At(ClayPeriodKind kind, DateTime anchor)
    {
        var date = anchor.Date;
        if (kind == ClayPeriodKind.Day) return new(kind, date, date);
        if (kind == ClayPeriodKind.Week)
        {
            var start = date.AddDays(-((int)date.DayOfWeek + 6) % 7);
            return new(kind, start, start > DateTime.MaxValue.Date.AddDays(-6) ? DateTime.MaxValue.Date : start.AddDays(6));
        }
        var month = kind switch { ClayPeriodKind.Year => 1, ClayPeriodKind.Quarter => (date.Month - 1) / 3 * 3 + 1, _ => date.Month };
        var endMonth = kind switch { ClayPeriodKind.Year => 12, ClayPeriodKind.Quarter => month + 2, _ => month };
        return new(kind, new(date.Year, month, 1), new(date.Year, endMonth, DateTime.DaysInMonth(date.Year, endMonth)));
    }
}
