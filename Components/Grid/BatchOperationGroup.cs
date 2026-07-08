namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Группа операций в меню групповых операций ClayGrid (подменю).
/// </summary>
public class BatchOperationGroup
{
    /// <summary>Заголовок группы (например, «Печать», «Мои операции»).</summary>
    public string Label { get; init; } = "";

    /// <summary>Иконка группы (Material Icons). Если <c>null</c> — без иконки.</summary>
    public string? Icon { get; init; }

    /// <summary>Список операций в этой группе.</summary>
    public IReadOnlyList<BatchOperation> Operations { get; init; } = [];
}
