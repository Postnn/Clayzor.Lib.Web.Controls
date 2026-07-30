using Clayzor.Lib.Web.Controls.Components.Grid;

namespace Clayzor.Lib.Web.Controls.Components.Filter;

/// <summary>
/// Условие фильтрации по одной SQL-колонке.
/// Поддерживает до двух условий, объединяемых через <see cref="LogicalOperator"/>.
/// Является листовым узлом в дереве составного фильтра (<see cref="IClayFilterNode"/>).
/// </summary>
public sealed class ColumnFilter : IClayFilterNode
{
    /// <summary>SQL-имя колонки (например, "НазваниеАнализа" или "a.НазваниеАнализа").</summary>
    public string Column { get; set; } = "";

    /// <summary>Имя Dapper-параметра для значения первого условия (без @, уникальное в запросе).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ParamName { get; set; } = "";

    /// <summary>Значение первого условия. null или пустая строка — условие не активно.</summary>
    public object? Value { get; set; }

    /// <summary>Оператор сравнения первого условия.</summary>
    public ColumnFilterOperator Operator { get; set; } = ColumnFilterOperator.Contains;

    /// <summary>Источник происхождения — для маршрутизации редактирования чипа в трее.</summary>
    public ClayFilterSource Source { get; set; } = ClayFilterSource.ColumnDialog;

    /// <summary>Возвращает true, если первое условие имеет значимое значение.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasValue => Operator is ColumnFilterOperator.IsEmpty or ColumnFilterOperator.IsNotEmpty
        or ColumnFilterOperator.IsNull or ColumnFilterOperator.IsNotNull
        || (Value is not null && Value.ToString() is { Length: > 0 });

    // ── Второе условие (опционально) ──────────────────────────────────────────────

    /// <summary>
    /// Логический оператор между первым и вторым условием.
    /// Игнорируется, если <see cref="HasSecondClause"/> = false.
    /// </summary>
    public LogicalOperator LogicalOperator { get; set; } = LogicalOperator.And;

    /// <summary>Имя Dapper-параметра для значения второго условия (без @).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SecondParamName { get; set; } = "";

    /// <summary>Значение второго условия. null — второе условие не активно.</summary>
    public object? SecondValue { get; set; }

    /// <summary>Оператор сравнения второго условия.</summary>
    public ColumnFilterOperator SecondOperator { get; set; } = ColumnFilterOperator.Contains;

    /// <summary>Возвращает true, если второе условие задано и имеет значение.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSecondClause => SecondOperator is ColumnFilterOperator.IsEmpty or ColumnFilterOperator.IsNotEmpty
        or ColumnFilterOperator.IsNull or ColumnFilterOperator.IsNotNull
        || (SecondValue is not null && SecondValue.ToString() is { Length: > 0 });

    /// <summary>
    /// Транзиентный UI-флаг: свежедобавленное условие (например, перетаскиванием колонки
    /// в составной фильтр) → редактор сразу фокусирует поле значения. Не сериализуется
    /// и не копируется в <see cref="Clone"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNew { get; set; }

    /// <summary>Глубокое копирование листового условия (оба значения и Source).</summary>
    public IClayFilterNode Clone() => new ColumnFilter
    {
        Column = Column,
        ParamName = ParamName,
        Operator = Operator,
        Value = Value,
        LogicalOperator = LogicalOperator,
        SecondParamName = SecondParamName,
        SecondOperator = SecondOperator,
        SecondValue = SecondValue,
        Source = Source,
    };
}
