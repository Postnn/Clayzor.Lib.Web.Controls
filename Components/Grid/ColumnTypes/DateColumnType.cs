using System.Globalization;

namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>Дескриптор колонки даты (DateTime/DateTimeOffset/DateOnly).</summary>
public sealed class DateColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Date;
    public override Type ClrType => typeof(DateTime);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.DateOperators;

    public override object? Parse(string? raw)
        => raw is not null && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;

    public override string Format(object? value)
        => value is DateTime dt ? dt.ToString("dd.MM.yyyy") : value?.ToString() ?? "";
}
