using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Маппинг целочисленного типа колонки из БД (<see cref="ClayColumnKind"/>) в
/// существующий <see cref="ColumnTypeDescriptor"/>.
/// </summary>
public static class ClayColumnTypeMap
{
    private static readonly ClayListColumnType _list = new();
    private static readonly ClayIconColumnType _icon = new();
    private static readonly ClayConditionBoolColumnType _conditionBool = new();
    private static readonly ClayConditionListColumnType _conditionList = new();
    private static readonly ClayHtmlColumnType _html = new();
    private static readonly ClayLimitedTextColumnType _limitedText = new();

    /// <summary>
    /// Возвращает дескриптор для поддерживаемых типов.
    /// Для неподдержанных (6,8,10–13) возвращает null.
    /// </summary>
    public static ColumnTypeDescriptor? Resolve(int type) => type switch
    {
        (int)ClayColumnKind.Number => ColumnTypeRegistry.FromKind(ColumnType.Number),
        (int)ClayColumnKind.Text   => ColumnTypeRegistry.FromKind(ColumnType.Text),
        (int)ClayColumnKind.Date   => ColumnTypeRegistry.FromKind(ColumnType.Date),
        (int)ClayColumnKind.Link   => ColumnTypeRegistry.FromKind(ColumnType.Text), // Link пока как Text
        (int)ClayColumnKind.List   => _list,
        (int)ClayColumnKind.Icon           => _icon,
        (int)ClayColumnKind.ConditionBool  => _conditionBool,
        (int)ClayColumnKind.Bool           => ColumnTypeRegistry.FromKind(ColumnType.Boolean),
        (int)ClayColumnKind.ConditionList  => _conditionList,
        (int)ClayColumnKind.Html          => _html,
        (int)ClayColumnKind.LimitedText   => _limitedText,
        _ => null
    };

    /// <summary>
    /// Возвращает true, если тип поддерживается (Resolve не null).
    /// </summary>
    public static bool IsSupported(int type) => Resolve(type) is not null;
}
