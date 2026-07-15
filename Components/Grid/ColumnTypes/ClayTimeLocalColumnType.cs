namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Дескриптор колонки типа «Время локализованное» (Тип 13).
/// Значение в UTC, отображается как локальное время.
/// Формат = .NET-формат времени (напр. "HH:mm").
/// </summary>
public sealed class ClayTimeLocalColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Date;
    public override Type ClrType => typeof(DateTime);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.DateOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Equals;

    public override object? Parse(string? raw)
        => raw is not null && DateTime.TryParse(raw, out var d) ? d : null;

    public override string Format(object? value) => value?.ToString() ?? "";
}
