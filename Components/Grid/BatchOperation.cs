namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Одна операция в меню групповых операций ClayGrid.
/// </summary>
public class BatchOperation
{
    /// <summary>Отображаемое название (например, «Текущая страница», «Выбранные»).</summary>
    public string Label { get; init; } = "";

    /// <summary>Иконка операции (Material Icons). Если <c>null</c> — без иконки.</summary>
    public string? Icon { get; init; }

    /// <summary>Показывать только когда есть выбранные строки.</summary>
    public bool RequiresSelection { get; init; }

    /// <summary>Показывать когда ничего не выбрано ИЛИ выбраны все строки на странице.</summary>
    public bool RequiresAll { get; init; }

    /// <summary>Обработчик — вызывается при выборе операции из меню.</summary>
    public Func<Task>? OnExecute { get; init; }
}
