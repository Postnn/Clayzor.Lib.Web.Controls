using Clayzor.Lib.Web.Controls.Components.Filter;
using Clayzor.Lib.Web.Controls.Components.Grid;
using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

namespace Clayzor.Lib.Web.Controls.Components.Tree.Helpers;

/// <summary>
/// Строит список <see cref="ClayFilterColumnInfo"/> для диалога настраиваемого фильтра дерева
/// из явно заданных <see cref="ClayTreeFilterColumn"/>. Колонки из <paramref name="excludedColumns"/>
/// исключаются (сравнение SqlName регистронезависимо). Дубли по SqlName удаляются (остаётся первое).
/// </summary>
public static class ClayTreeFilterColumnBuilder
{
    /// <summary>
    /// Строит список фильтруемых полей дерева.
    /// </summary>
    /// <param name="columns">Явный список колонок из опций; null или пустой — фильтр недоступен.</param>
    /// <param name="excludedColumns">SQL-имена колонок, исключаемых из списка (регистронезависимо).</param>
    /// <returns>Готовый список для <see cref="ClayFilterDialog"/>.</returns>
    public static IReadOnlyList<ClayFilterColumnInfo> Build(
        IReadOnlyList<ClayTreeFilterColumn>? columns,
        IReadOnlyList<string> excludedColumns)
    {
        if (columns is null || columns.Count == 0)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = excludedColumns is { Count: > 0 }
            ? new HashSet<string>(excludedColumns, StringComparer.OrdinalIgnoreCase)
            : null;

        var result = new List<ClayFilterColumnInfo>(columns.Count);

        foreach (var col in columns)
        {
            if (string.IsNullOrWhiteSpace(col.SqlName))
                continue;

            // Проверка на исключение
            if (excluded is not null && excluded.Contains(col.SqlName))
                continue;

            // Дедупликация по SqlName
            if (!seen.Add(col.SqlName))
                continue;

            result.Add(new ClayFilterColumnInfo
            {
                SqlName        = col.SqlName,
                DisplayName    = col.DisplayName,
                Type           = ColumnTypeRegistry.FromKind(col.ColumnType),
                Options        = col.Options,
                BoolTrueLabel  = col.BoolTrueLabel,
                BoolFalseLabel = col.BoolFalseLabel,
            });
        }

        return result;
    }
}
