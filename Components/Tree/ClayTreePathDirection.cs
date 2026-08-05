namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Направление построения полного пути к узлу скалярной SQL-функцией.
/// Значение передаётся вторым параметром функции (@PathType bit).
/// </summary>
public enum ClayTreePathDirection
{
    /// <summary>От потомка к родителю (@PathType = 0).</summary>
    ChildToParent = 0,

    /// <summary>От родителя к потомку (@PathType = 1). Значение по умолчанию.</summary>
    ParentToChild = 1,
}
