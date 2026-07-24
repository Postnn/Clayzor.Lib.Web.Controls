namespace Clayzor.Lib.Web.Controls.Components.Tree.Models;

/// <summary>
/// UI-модель одного узла дерева. Содержит как данные, так и состояние раскрытия/загрузки.
/// </summary>
public sealed class ClayTreeNode
{
    /// <summary>Строковый ключ узла для состояния и сравнения.</summary>
    public required string Id { get; init; }

    /// <summary>Исходный идентификатор из БД — уходит параметром в SQL.</summary>
    public object? RawId { get; init; }

    /// <summary>Текстовое представление узла.</summary>
    public string Text { get; set; } = "";

    /// <summary>Идентификатор родителя (режим ParentKey).</summary>
    public object? ParentId { get; init; }

    /// <summary>Уровень вложенности (0 = корень).</summary>
    public int Level { get; set; }

    /// <summary>Левый ключ вложенных множеств (режим NestedSet).</summary>
    public long? Left { get; init; }

    /// <summary>Правый ключ вложенных множеств (режим NestedSet).</summary>
    public long? Right { get; init; }

    /// <summary>Есть ли у узла дочерние элементы.</summary>
    public bool HasChildren { get; init; }

    /// <summary>Раскрыт ли узел в данный момент.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>Был ли загружен уровень детей (хотя бы один раз).</summary>
    public bool IsLoaded { get; set; }

    /// <summary>Идёт ли загрузка детей в данный момент.</summary>
    public bool IsLoading { get; set; }

    /// <summary>Загруженные дочерние узлы.</summary>
    public List<ClayTreeNode> Children { get; set; } = [];

    /// <summary>Ссылка на родительский узел.</summary>
    public ClayTreeNode? Parent { get; set; }

    /// <summary>Дополнительные колонки строки — задел на будущее (контекстное меню, фильтры).</summary>
    public IReadOnlyDictionary<string, object?> Raw { get; init; } = new Dictionary<string, object?>();
}
