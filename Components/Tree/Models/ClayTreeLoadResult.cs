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
    long? NextCursor = null)
{
    /// <summary>
    /// Применяет логику кейсет-пагинации к загруженным узлам.
    /// Запрошено <c>TOP(@pageSize + 1)</c> — если пришло больше pageSize,
    /// лишняя строка отбрасывается, <c>HasMore=true</c>, <c>NextCursor</c> = L последней оставленной.
    /// Чистая функция — тестируется без БД.
    /// </summary>
    public static ClayTreeLoadResult FromPagedRows(List<ClayTreeNode> nodes, int pageSize)
    {
        if (nodes.Count > pageSize)
        {
            nodes.RemoveAt(nodes.Count - 1);
            var lastNode = nodes[^1];
            return new ClayTreeLoadResult(nodes, HasMore: true, NextCursor: lastNode.Left);
        }

        var last = nodes.Count > 0 ? nodes[^1] : null;
        return new ClayTreeLoadResult(nodes, HasMore: false, NextCursor: last?.Left);
    }
}
