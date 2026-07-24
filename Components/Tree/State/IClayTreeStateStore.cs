using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree.State;

/// <summary>
/// Абстракция хранилища состояния дерева. В CT1 — одна реализация в памяти (Scoped).
/// CT5 добавит персистенцию в БД.
/// </summary>
public interface IClayTreeStateStore
{
    /// <summary>Загружает состояние дерева по идентификатору.</summary>
    Task<ClayTreeState?> LoadAsync(string treeId, CancellationToken ct = default);

    /// <summary>Сохраняет состояние дерева.</summary>
    Task SaveAsync(string treeId, ClayTreeState state, CancellationToken ct = default);
}
