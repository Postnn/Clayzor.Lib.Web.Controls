namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Снапшот настроек, влияющих на <see cref="IClayTreeMutations"/>.
/// При изменении любого поля кэшированный экземпляр мутаций пересоздаётся.
/// Пустая строка эквивалентна null — те же проверки <c>string.IsNullOrEmpty</c>, что в компоненте.
/// </summary>
internal readonly record struct ClayTreeMutationsKey(
    string? TableName,
    string? ConnectionStringName,
    string IdColumn,
    string? ParentColumn,
    string? LeftColumn,
    string? RightColumn)
{
    /// <summary>
    /// Строит ключ из текущих <see cref="ClayTreeOptions"/>.
    /// </summary>
    public static ClayTreeMutationsKey From(ClayTreeOptions options) => new(
        Normalize(options.TableName),
        Normalize(options.ConnectionStringName),
        options.Schema.IdColumn,
        options.Schema.ParentColumn,
        options.Schema.LeftColumn,
        options.Schema.RightColumn);

    private static string? Normalize(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
