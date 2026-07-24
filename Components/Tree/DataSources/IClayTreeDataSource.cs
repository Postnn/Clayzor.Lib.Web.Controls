using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree.DataSources;

/// <summary>
/// Абстракция источника данных дерева. Позволяет подменить реальный SQL-источник
/// на тестовый или нестандартный.
/// </summary>
public interface IClayTreeDataSource
{
    /// <summary>Загружает один уровень дерева.</summary>
    Task<ClayTreeLoadResult> LoadLevelAsync(ClayTreeLoadRequest request, CancellationToken ct = default);
}
