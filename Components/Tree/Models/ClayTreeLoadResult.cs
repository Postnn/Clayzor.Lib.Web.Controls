namespace Clayzor.Lib.Web.Controls.Components.Tree.Models;

/// <summary>Результат загрузки одного уровня дерева.</summary>
/// <param name="Nodes">Загруженные узлы. Пустой список — детей нет.</param>
/// <param name="Error">Сообщение об ошибке. <c>null</c> — загрузка успешна.</param>
public sealed record ClayTreeLoadResult(IReadOnlyList<ClayTreeNode> Nodes, string? Error = null);
