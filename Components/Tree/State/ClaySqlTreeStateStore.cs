using System.Text.Json;
using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Tree.State;

/// <summary>
/// Персистентное хранилище состояния дерева в таблице пользовательских параметров (та же, что у грида).
/// Сохраняет два ключа на дерево: якорь (LastExpandedId) и выделение (SelectedIds).
/// CLID — из query-строки по ClientIdQueryParam из ClayGridDynamicSettings.
/// </summary>
public sealed class ClaySqlTreeStateStore : IClayTreeStateStore
{
    private readonly DbManager _db;
    private readonly IOptions<ClayGridDynamicSettings> _gridSettings;
    private readonly IOptions<ClayTreeDynamicSettings> _treeSettings;
    private readonly NavigationManager _nav;

    public ClaySqlTreeStateStore(
        DbManager db,
        IOptions<ClayGridDynamicSettings> gridSettings,
        IOptions<ClayTreeDynamicSettings> treeSettings,
        NavigationManager nav)
    {
        _db = db;
        _gridSettings = gridSettings;
        _treeSettings = treeSettings;
        _nav = nav;
    }

    /// <inheritdoc/>
    public async Task<ClayTreeState?> LoadAsync(string treeId, CancellationToken ct = default)
    {
        var (anchorName, selName) = BuildParamNames(treeId);
        var clid = ResolveClid();
        var schema = _gridSettings.Value.Schema;

        var sql = ClayGridUserParamsData.BuildLoadSql(_gridSettings.Value.UserParamsTable, schema, 2);
        var dp = new Dapper.DynamicParameters();
        dp.Add("clid", clid);
        dp.Add("shid", 0);
        dp.Add("n0", anchorName);
        dp.Add("n1", selName);

        var rows = await DynamicSql.QueryRowsAsync(_db, sql, dp, ct: ct);
        if (rows.Count == 0)
            return null;

        var dict = rows.ToDictionary(
            r => r.GetValueOrDefault(schema.UserParams.Name)?.ToString() ?? "",
            r => r.GetValueOrDefault(schema.UserParams.Value)?.ToString() ?? "");

        var state = new ClayTreeState();

        if (dict.TryGetValue(anchorName, out var anchor) && !string.IsNullOrEmpty(anchor))
            state.LastExpandedId = anchor;

        if (dict.TryGetValue(selName, out var selJson) && !string.IsNullOrEmpty(selJson))
        {
            try { state.SelectedIds = JsonSerializer.Deserialize<HashSet<string>>(selJson) ?? []; }
            catch { /* миграция: старый формат → пусто */ }
        }

        return state;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string treeId, ClayTreeState state, CancellationToken ct = default)
    {
        var (anchorName, selName) = BuildParamNames(treeId);
        var clid = ResolveClid();

        var sql = ClayGridUserParamsData.BuildInsertSql(_gridSettings.Value.UserParamsTable, _gridSettings.Value.Schema);
        var shid = 0;

        // Якорь
        var anchorDp = new Dapper.DynamicParameters();
        anchorDp.Add("clid", clid);
        anchorDp.Add("name", anchorName);
        anchorDp.Add("value", (object?)state.LastExpandedId ?? DBNull.Value);
        anchorDp.Add("shid", shid);
        await _db.ExecuteAsync(sql, anchorDp, commandType: System.Data.CommandType.Text);

        // Выделение
        var selJson = JsonSerializer.Serialize(state.SelectedIds);
        var selDp = new Dapper.DynamicParameters();
        selDp.Add("clid", clid);
        selDp.Add("name", selName);
        selDp.Add("value", selJson);
        selDp.Add("shid", shid);
        await _db.ExecuteAsync(sql, selDp, commandType: System.Data.CommandType.Text);
    }

    /// <summary>Строит имена параметров: {prefix}{hash} и {prefix}{hash}_sel. Укладывается в varchar(20).</summary>
    private (string anchor, string sel) BuildParamNames(string treeId)
    {
        var hash = Math.Abs(treeId.GetHashCode()).ToString("X6");
        var prefix = _treeSettings.Value.StateParamPrefix;
        var anchor = $"{prefix}{hash}";
        var sel = $"{prefix}{hash}_s";
        if (anchor.Length > 20)
            anchor = anchor[..20];
        if (sel.Length > 20)
            sel = sel[..20];
        return (anchor, sel);
    }

    private string ResolveClid()
    {
        var uri = new Uri(_nav.Uri);
        var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var key = _gridSettings.Value.ClientIdQueryParam;
        if (string.IsNullOrEmpty(key)) return "0";
        return qs[key] ?? "0";
    }
}
