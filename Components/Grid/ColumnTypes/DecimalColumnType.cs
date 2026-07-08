using System.Globalization;

namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>Дескриптор дробной колонки (decimal/double/float).</summary>
public sealed class DecimalColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Decimal;
    public override Type ClrType => typeof(decimal);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.DecimalOperators;

    public override object? Parse(string? raw)
        => raw is not null && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    public override string Format(object? value)
        => value is decimal d ? d.ToString("G", CultureInfo.InvariantCulture) : value?.ToString() ?? "";
}
