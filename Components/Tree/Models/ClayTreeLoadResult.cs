namespace Clayzor.Lib.Web.Controls.Components.Tree.Models;

/// <summary>Результат загрузки одного уровня дерева.</summary>
/// <param name="Nodes">Загруженные узлы. Пустой список — детей нет.</param>
/// <param name="Error">Сообщение об ошибке. <c>null</c> — загрузка успешна.</param>
/// <param name="HasMore">Есть ли ещё непрочитанные дети уровня (пришло больше PageSize).</param>
/// <param name="NextCursor">Курсор для следующей порции — L последнего возвращаемого ребёнка.</param>
public sealed record ClayTreeLoadResult(
    IReadOnlyList<ClayTreeNode> Nodes,
    string? Error = null,
    bool HasMore = false,
    long? NextCursor = null);
