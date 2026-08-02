using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>Рекурсивный рендер одного узла дерева и его детей.</summary>
public partial class ClayTreeNodeView : ComponentBase, IDisposable
{
    /// <summary>Отображаемый узел.</summary>
    [Parameter, EditorRequired] public ClayTreeNode Node { get; set; } = null!;

    /// <summary>Ссылка на дерево для вызова операций раскрытия/сворачивания.</summary>
    [Parameter, EditorRequired] public IClayTreeView Tree { get; set; } = null!;

    /// <summary>Пользовательский шаблон содержимого узла.</summary>
    [Parameter] public RenderFragment<ClayTreeNode>? NodeTemplate { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = null!;

    private ElementReference _sentinel;
    private DotNetObjectReference<ClayTreeNodeView>? _selfRef;
    private bool _observing;

    private async Task HandleToggle()
    {
        if (Tree is ClayTreeView view)
            await view.ToggleAsync(Node);
    }

    private async Task HandleClick()
    {
        if (Tree is ClayTreeView view)
            await view.HandleNodeClick(Node);
    }

    private async Task HandleLoadMore()
    {
        if (Tree is ClayTreeView view)
            await view.LoadMoreChildrenAsync(Node);
    }

    /// <summary>Вызывается из JS IntersectionObserver при попадании сентинела во вьюпорт.</summary>
    [JSInvokable]
    public async Task OnSentinelVisible()
    {
        if (Tree is ClayTreeView view)
            await view.LoadMoreChildrenAsync(Node);
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Tree is ClayTreeView { Options.LevelPagingMode: ClayTreeLevelPagingMode.Scroll }
            && !Node.LoadedAllChildren && Tree.LevelPageSize > 0)
        {
            if (!_observing)
            {
                _selfRef = DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("clayTreePaging.observe", _sentinel, _selfRef);
                _observing = true;
            }
        }
        else if (_observing)
        {
            await JS.InvokeVoidAsync("clayTreePaging.unobserve", _sentinel);
            _selfRef?.Dispose();
            _selfRef = null;
            _observing = false;
        }
    }

    /// <summary>Освобождает JS-наблюдателя. Разрыв circuit'а — штатная ситуация, не ошибка.</summary>
    public void Dispose()
    {
        if (_observing)
        {
            try
            {
                _ = JS.InvokeVoidAsync("clayTreePaging.unobserve", _sentinel);
            }
            catch (JSDisconnectedException) { /* circuit уже закрыт */ }
            catch (ObjectDisposedException) { /* JS-рантайм освобождён */ }
            catch (InvalidOperationException) { /* prerendering / нет JS */ }
        }
        _selfRef?.Dispose();
    }
}
