namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Группа условий составного фильтра с логикой И/ИЛИ.
/// Может содержать листы (<see cref="ColumnFilter"/>) и вложенные группы.
/// </summary>
public sealed class ClayFilterGroupNode : IClayFilterNode
{
    /// <summary>Логический оператор, применяемый к прямым дочерним узлам.</summary>
    public LogicalOperator Logic { get; set; } = LogicalOperator.And;

    /// <summary>Дочерние узлы: листы (<see cref="ColumnFilter"/>) и/или вложенные группы.</summary>
    public List<IClayFilterNode> Nodes { get; set; } = new();

    /// <summary>Рекурсивное глубокое копирование группы и всех дочерних узлов.</summary>
    public IClayFilterNode Clone() => new ClayFilterGroupNode
    {
        Logic = Logic,
        Nodes = Nodes.Select(n => n.Clone()).ToList()
    };
}
