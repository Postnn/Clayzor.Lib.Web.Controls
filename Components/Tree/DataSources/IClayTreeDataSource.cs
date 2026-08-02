using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Tree.DataSources;

/// <summary>
/// Абстракция источника данных дерева. Позволяет подменить реальный SQL-источник
/// на тестовый или нестандартный.
/// </summary>
public interface IClayTreeDataSource
{
    /// <summary>Загружает один уровень дерева.</summary>
    Task<ClayTreeLoadResult> LoadLevelAsync(ClayTreeLoadRequest request, CancellationToken ct = default);

    /// <summary>
    /// Загружает набор узлов в режиме фильтра: совпадения + все их предки с флагами.
    /// Реализация по умолчанию бросает <see cref="NotSupportedException"/> —
    /// переопределяется в <see cref="ClaySqlTreeDataSource"/>.
    /// </summary>
    async Task<ClayTreeLoadResult> LoadFilteredAsync(string whereClause, DynamicParameters dp, int max, CancellationToken ct = default)
    {
        await Task.CompletedTask; // для async-сигнатуры
        throw new NotSupportedException("LoadFilteredAsync не поддерживается этим источником данных.");
    }
}
