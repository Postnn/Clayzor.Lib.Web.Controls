namespace Clayzor.Lib.Web.Controls.Components.Tree.Models;

/// <summary>Запрос на загрузку одного уровня дерева.</summary>
/// <param name="Parent">Узел, для которого загружаются дети. <c>null</c> — корневой уровень.</param>
public sealed record ClayTreeLoadRequest(ClayTreeNode? Parent);
