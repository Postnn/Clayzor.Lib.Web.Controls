using System.Text.Json.Serialization;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Узел дерева составного фильтра: лист (<see cref="ColumnFilter"/>) или группа (<see cref="ClayFilterGroupNode"/>).
/// </summary>
[JsonConverter(typeof(ClayFilterJsonConverter))]
public interface IClayFilterNode
{
    /// <summary>Рекурсивное глубокое копирование узла. Правка копии не трогает оригинал.</summary>
    IClayFilterNode Clone();
}
