namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Дескриптор колонки типа «Условие булево» (Тип 6).
/// Фильтр-онли: не выводится в гриде и группировке, только фильтрация (вкл/выкл).
/// Формат = SQL-условие, которое добавляется в WHERE при включении.
/// </summary>
public sealed class ClayConditionBoolColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Boolean;
    public override Type ClrType => typeof(bool);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.BooleanOperators;

    public override object? Parse(string? raw) => raw is not null && (raw == "1" || bool.TryParse(raw, out var b) && b);

    public override string Format(object? value) => value is true ? "Да" : "Нет";
}
