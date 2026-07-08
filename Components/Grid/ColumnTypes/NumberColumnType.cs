using System.Globalization;

namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>Дескриптор целочисленной колонки.</summary>
public sealed class NumberColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Number;
    public override Type ClrType => typeof(int);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.NumberOperators;

    public override object? Parse(string? raw)
        => raw is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public override string Format(object? value)
        => value is int i ? i.ToString(CultureInfo.InvariantCulture) : "";
}
