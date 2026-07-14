namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Дескриптор колонки типа «Список» (Тип 5).
/// Значение — код из справочника, текст для отображения — из подзапроса.
/// Формат = SQL (1-я колонка — value, 2-я — text).
/// </summary>
public sealed class ClayListColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Text;
    public override Type ClrType => typeof(string);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.TextOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Equals;

    public override object? Parse(string? raw) => raw;

    public override string Format(object? value) => value?.ToString() ?? "";
}
