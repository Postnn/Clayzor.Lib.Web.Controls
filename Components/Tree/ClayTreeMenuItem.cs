using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Пользовательский пункт контекстного меню узла дерева.
/// </summary>
public sealed class ClayTreeMenuItem
{
    /// <summary>Подпись пункта меню. Обязательна.</summary>
    public required string Label { get; init; }

    /// <summary>Иконка (значение из <c>MudBlazor.Icons</c>). null — без иконки.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Действие при выборе пункта. Получает узел, на котором открыто меню.
    /// Обновление данных/дерева при необходимости выполняет само приложение
    /// (например, вызовом <c>ReloadAsync</c> через ссылку на дерево).
    /// </summary>
    public required Func<ClayTreeNode, Task> OnExecute { get; init; }
}
