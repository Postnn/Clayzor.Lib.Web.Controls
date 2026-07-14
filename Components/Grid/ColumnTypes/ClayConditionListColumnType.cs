namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Дескриптор колонки типа «Условие список» (Тип 11).
/// Фильтр-онли: не выводится в гриде и группировке, только фильтрация выбором из списка.
/// Формат = строки <c>Название условия\tSQL-выражение</c>; выбранные whereExpr добавляются в WHERE.
/// </summary>
public sealed class ClayConditionListColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Text;
    public override Type ClrType => typeof(string);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.TextOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Equals;

    public override object? Parse(string? raw) => raw;

    public override string Format(object? value) => value?.ToString() ?? "";
}
