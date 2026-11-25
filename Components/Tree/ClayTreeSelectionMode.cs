namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Режим выделения узлов дерева <see cref="ClayTreeView"/>.
/// </summary>
public enum ClayTreeSelectionMode
{
    /// <summary>Выделение выключено.</summary>
    None = 0,

    /// <summary>Одиночное выделение (клик по узлу).</summary>
    Single = 1,

    /// <summary>Управляемый множественный выбор с отметками узлов.</summary>
    Multiple = 2,
}
