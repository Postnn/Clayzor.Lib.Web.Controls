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

    // Multiple = 2 — задел под множественный выбор (чекбоксы), в текущей версии не реализовано
}
