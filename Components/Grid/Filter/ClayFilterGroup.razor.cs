using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Рекурсивный компонент узла-группы составного фильтра.
/// Отображает переключатель И/ИЛИ, дочерние условия/группы и кнопки добавления.
/// </summary>
public partial class ClayFilterGroup : ComponentBase
{
    // ── Параметры ──────────────────────────────────────────────────────────────

    /// <summary>Редактируемый узел-группа.</summary>
    [Parameter, EditorRequired]
    public ClayFilterGroupNode Node { get; set; } = null!;

    /// <summary>Список доступных для фильтрации колонок.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<ClayColumnMeta> Columns { get; set; } = [];

    /// <summary>Необязательные варианты значений на колонку (SqlName → список).</summary>
    [Parameter]
    public IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>>? LookupOptions { get; set; }

    /// <summary>
    /// Признак корневой группы. Корневая группа не имеет кнопки удаления.
    /// </summary>
    [Parameter]
    public bool IsRoot { get; set; } = false;

    /// <summary>Вызывается при любом изменении внутри группы.</summary>
    [Parameter]
    public EventCallback OnChanged { get; set; }

    /// <summary>Вызывается при нажатии кнопки удаления группы (только для не-корневых).</summary>
    [Parameter]
    public EventCallback OnRemove { get; set; }

    // ── Обработчики ───────────────────────────────────────────────────────────

    /// <summary>Переключает логический оператор группы.</summary>
    private async Task SetLogic(LogicalOperator logic)
    {
        Node.Logic = logic;
        await NotifyChanged();
    }

    /// <summary>
    /// Добавляет новое пустое листовое условие с <c>Source=CompositeDialog</c>.
    /// Выбирается первая доступная колонка и её оператор по умолчанию.
    /// </summary>
    private async Task AddExpression()
    {
        var firstCol = Columns.FirstOrDefault();
        var newLeaf  = new ColumnFilter
        {
            Column    = firstCol?.SqlName ?? "",
            ParamName = "",   // ParamName генерируется при построении SQL (задача 07/10)
            Operator  = firstCol?.Type.DefaultOperator ?? ColumnFilterOperator.Contains,
            Source    = ClayFilterSource.CompositeDialog,
        };
        Node.Nodes.Add(newLeaf);
        await NotifyChanged();
    }

    /// <summary>Добавляет новую вложенную пустую группу.</summary>
    private async Task AddGroup()
    {
        Node.Nodes.Add(new ClayFilterGroupNode());
        await NotifyChanged();
    }

    /// <summary>Удаляет дочерний узел по индексу.</summary>
    private async Task RemoveNode(int index)
    {
        if (index >= 0 && index < Node.Nodes.Count)
        {
            Node.Nodes.RemoveAt(index);
            await NotifyChanged();
        }
    }

    /// <summary>Всплывает событие <see cref="OnChanged"/> к родителю.</summary>
    private async Task NotifyChanged()
    {
        StateHasChanged();
        await OnChanged.InvokeAsync();
    }

    /// <summary>
    /// Возвращает краткое читаемое описание листового условия для отображения
    /// в блоке «редактируется в диалоге колонки».
    /// </summary>
    private string GetLeafDescription(ColumnFilter leaf)
        => ClayFilterDescriptionBuilder.DescribeLeaf(
            leaf,
            sql => Columns.FirstOrDefault(c => c.SqlName == sql)?.DisplayName ?? sql);

    /// <summary>
    /// Возвращает подпись для фильтра по значению в диалоге настраиваемого фильтра.
    /// Использует <see cref="ClayFilterDescriptionBuilder.DescribeValueFilter"/>
    /// для форматирования значений и учёта режима IN/NOT IN.
    /// </summary>
    private string ValueFilterLabel(ValueFilter vf)
        => ClayFilterDescriptionBuilder.DescribeValueFilter(
            vf,
            sql => Columns.FirstOrDefault(c => c.SqlName == sql)?.DisplayName ?? sql,
            sql => Columns.FirstOrDefault(c => c.SqlName == sql));
}
