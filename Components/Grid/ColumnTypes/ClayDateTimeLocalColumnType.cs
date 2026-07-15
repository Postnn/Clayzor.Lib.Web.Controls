namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Дескриптор колонки типа «Дата-время локализованное» (Тип 10).
/// Значение в UTC, отображается в локальном часовом поясе.
/// Формат = .NET-формат даты (напр. "dd.MM.yyyy HH:mm").
/// </summary>
public sealed class ClayDateTimeLocalColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Date;
    public override Type ClrType => typeof(DateTime);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.DateOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Equals;

    public override object? Parse(string? raw)
        => raw is not null && DateTime.TryParse(raw, out var d) ? d : null;

    public override string Format(object? value) => value?.ToString() ?? "";
}
