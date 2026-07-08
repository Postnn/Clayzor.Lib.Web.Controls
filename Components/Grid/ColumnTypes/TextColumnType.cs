namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>Дескриптор текстовой колонки.</summary>
public sealed class TextColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Text;
    public override Type ClrType => typeof(string);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.TextOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Contains;

    public override object? Parse(string? raw) => raw;
    public override string Format(object? value) => value?.ToString() ?? "";
}
