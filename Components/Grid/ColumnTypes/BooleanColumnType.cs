namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>Дескриптор булевой колонки.</summary>
public sealed class BooleanColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Boolean;
    public override Type ClrType => typeof(bool);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.BooleanOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Equals;

    public override object? Parse(string? raw)
        => raw is not null && bool.TryParse(raw, out var v) ? v : null;

    public override string Format(object? value)
        => value is bool b ? (b ? "Да" : "Нет") : "";
}
