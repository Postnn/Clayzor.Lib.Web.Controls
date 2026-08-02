namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Строковые константы дерева — пометки фильтра, тултипы.
/// </summary>
public static class ClayTreeStrings
{
    /// <summary>Пометка «узел удовлетворяет фильтру».</summary>
    public const string MatchMark = "(!)";

    /// <summary>Пометка «среди потомков есть совпавшие с фильтром».</summary>
    public const string FilteredMark = "(отфильтровано)";

    /// <summary>Тултип пометки совпадения.</summary>
    public const string MatchTooltip = "Удовлетворяет фильтру";

    /// <summary>Тултип пометки «показаны не все потомки».</summary>
    public const string FilteredTooltip = "Показаны не все потомки";
}
