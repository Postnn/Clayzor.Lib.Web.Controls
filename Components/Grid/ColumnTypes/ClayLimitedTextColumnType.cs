namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Дескриптор колонки типа «Текст с ограничением длины» (Тип 12).
/// Формат = число (макс. длина). Если текст длиннее — обрезается и добавляется «…».
/// Полный текст доступен как tooltip.
/// </summary>
public sealed class ClayLimitedTextColumnType : ColumnTypeDescriptor
{
    public override ColumnType Kind => ColumnType.Text;
    public override Type ClrType => typeof(string);
    public override IReadOnlyList<ColumnFilterOperator> Operators => ColumnFilterOperatorList.TextOperators;
    public override ColumnFilterOperator DefaultOperator => ColumnFilterOperator.Contains;

    public override object? Parse(string? raw) => raw;

    public override string Format(object? value) => value?.ToString() ?? "";
}
