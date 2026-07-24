using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree.DataSources;

/// <summary>
/// Реализация источника данных через SQL: вызывает <see cref="ClayTreeData"/>,
/// маппит <see cref="ClayTreeRow"/> → <see cref="ClayTreeNode"/>.
/// </summary>
public sealed class ClaySqlTreeDataSource : IClayTreeDataSource
{
    private readonly DbManager _db;
    private readonly ClayTreeSource _source;

    /// <param name="db">Инжектированный менеджер БД.</param>
    /// <param name="source">Описание источника, собранное из <see cref="ClayTreeOptions"/>.</param>
    public ClaySqlTreeDataSource(DbManager db, ClayTreeSource source)
    {
        _db = db;
        _source = source;
    }

    /// <inheritdoc/>
    public async Task<ClayTreeLoadResult> LoadLevelAsync(ClayTreeLoadRequest request, CancellationToken ct = default)
    {
        try
        {
            ClayTreeRow? parentRow = null;
            if (request.Parent is not null)
            {
                parentRow = new ClayTreeRow
                {
                    Id    = request.Parent.RawId,
                    Left  = request.Parent.Left,
                    Right = request.Parent.Right,
                    Level = request.Parent.Level,
                };
            }

            var rows = await ClayTreeData.LoadLevelAsync(_db, _source, parentRow, ct);
            var nodes = rows.Select(r => MapRow(r, request.Parent)).ToList();
            return new ClayTreeLoadResult(nodes);
        }
        catch (Exception ex)
        {
            return new ClayTreeLoadResult([], ex.Message);
        }
    }

    private ClayTreeNode MapRow(ClayTreeRow row, ClayTreeNode? parent)
    {
        return new ClayTreeNode
        {
            Id          = ToKey(row.Id),
            RawId       = row.Id,
            Text        = row.Text,
            ParentId    = row.ParentId,
            Level       = _source.Mode == ClayTreeHierarchyMode.ParentKey
                            ? (parent?.Level + 1) ?? 0
                            : row.Level ?? 0,
            Left        = row.Left,
            Right       = row.Right,
            HasChildren = row.HasChildren,
            Parent      = parent,
            Raw         = row.Raw,
        };
    }

    /// <summary>Преобразует идентификатор в строковый ключ для состояния.</summary>
    public static string ToKey(object? value) => value?.ToString() ?? "";
}
