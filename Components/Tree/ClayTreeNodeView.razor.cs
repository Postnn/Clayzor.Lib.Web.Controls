using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>Рекурсивный рендер одного узла дерева и его детей.</summary>
public partial class ClayTreeNodeView : ComponentBase
{
    /// <summary>Отображаемый узел.</summary>
    [Parameter, EditorRequired] public ClayTreeNode Node { get; set; } = null!;

    /// <summary>Ссылка на дерево для вызова операций раскрытия/сворачивания.</summary>
    [Parameter, EditorRequired] public IClayTreeView Tree { get; set; } = null!;

    /// <summary>Пользовательский шаблон содержимого узла.</summary>
    [Parameter] public RenderFragment<ClayTreeNode>? NodeTemplate { get; set; }

    private async Task HandleToggle()
    {
        if (Tree is ClayTreeView view)
            await view.ToggleAsync(Node);
    }

    private async Task HandleClick()
    {
        if (Tree is ClayTreeView view)
            await view.OnNodeClick.InvokeAsync(Node);
    }
}
