namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Дескриптор колонки типа «Пиктограмма» (Тип 9).
/// Значение — код, отображается как иконка с tooltip.
/// Формат = SQL (1 — value, 2 — tooltip, 3 — href иконки).
/// </summary>
public sealed class ClayIconColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Text;
    public override Type ClrType => typeof(string);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.TextOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Equals;

    public override object? Parse(string? raw) => raw;

    public override string Format(object? value) => value?.ToString() ?? "";
}
