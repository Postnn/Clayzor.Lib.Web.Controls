namespace Clayzor.Lib.Web.Controls.Components.Tree.Models;

/// <summary>
/// Состояние дерева: якорь последней раскрытой ноды и выделение.
/// Хранится в пользовательских параметрах по CLID. Заменяет старую модель «набор раскрытых веток».
/// </summary>
public sealed class ClayTreeState
{
    /// <summary>Идентификатор последней раскрытой пользователем ноды (якорь восстановления пути).</summary>
    public string? LastExpandedId { get; set; }

    /// <summary>Выделенные ноды. В текущей версии — не более одной (Single); набор — задел под Multiple.</summary>
    public HashSet<string> SelectedIds { get; set; } = [];
}
