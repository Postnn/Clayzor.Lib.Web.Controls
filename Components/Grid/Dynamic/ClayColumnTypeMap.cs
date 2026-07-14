using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Маппинг целочисленного типа колонки из БД (<see cref="ClayColumnKind"/>) в
/// существующий <see cref="ColumnTypeDescriptor"/>.
/// </summary>
public static class ClayColumnTypeMap
{
    /// <summary>
    /// Возвращает дескриптор для поддерживаемых типов (1,2,3,4,7).
    /// Для неподдержанных (5,6,8–13) возвращает null.
    /// </summary>
    public static ColumnTypeDescriptor? Resolve(int type) => type switch
    {
        (int)ClayColumnKind.Number => ColumnTypeRegistry.FromKind(ColumnType.Number),
        (int)ClayColumnKind.Text   => ColumnTypeRegistry.FromKind(ColumnType.Text),
        (int)ClayColumnKind.Date   => ColumnTypeRegistry.FromKind(ColumnType.Date),
        (int)ClayColumnKind.Link   => ColumnTypeRegistry.FromKind(ColumnType.Text), // Link пока как Text
        (int)ClayColumnKind.Bool   => ColumnTypeRegistry.FromKind(ColumnType.Boolean),
        _ => null
    };

    /// <summary>
    /// Возвращает true, если тип поддерживается (Resolve не null).
    /// </summary>
    public static bool IsSupported(int type) => Resolve(type) is not null;
}
