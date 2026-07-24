namespace Clayzor.Lib.Web.Controls.Components.Tree.Models;

/// <summary>
/// Состояние дерева. В CT1 — только набор раскрытых узлов.
/// Расширяется в CT2+ (выделение, чекбоксы).
/// </summary>
public sealed class ClayTreeState
{
    /// <summary>Строковые ключи раскрытых узлов.</summary>
    public HashSet<string> ExpandedIds { get; set; } = [];
}
