using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Контракт дерева для страницы и дочерних компонентов. Передаётся через <c>CascadingValue</c>.
/// </summary>
public interface IClayTreeView
{
    /// <summary>Уникальный идентификатор дерева.</summary>
    string TreeId { get; }

    /// <summary>Режим иерархии, заданный в настройках.</summary>
    ClayTreeHierarchyMode HierarchyMode { get; }

    /// <summary>Корневые узлы дерева.</summary>
    IReadOnlyList<ClayTreeNode> RootNodes { get; }

    /// <summary>Строковые ключи раскрытых в данный момент узлов.</summary>
    IReadOnlySet<string> ExpandedIds { get; }

    /// <summary>Величина отступа на уровень в пикселях.</summary>
    int IndentPx { get; }

    /// <summary>Полная перезагрузка дерева с сохранением раскрытого состояния.</summary>
    Task ReloadAsync();

    /// <summary>Раскрыть узел по строковому ключу (с ленивой загрузкой уровня).</summary>
    Task ExpandAsync(string id);

    /// <summary>Свернуть узел по строковому ключу.</summary>
    Task CollapseAsync(string id);
}
