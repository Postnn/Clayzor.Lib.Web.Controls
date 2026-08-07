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
        anchorDp.Add("value", (object?)state.LastExpandedId);
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

    /// <summary>
    /// Стабильный (между запусками процесса) 32-битный хеш FNV-1a.
    /// string.GetHashCode() использовать нельзя: в .NET он рандомизируется на каждый старт,
    /// а имя параметра — персистентный ключ в БД.
    /// </summary>
    internal static uint StableHash(string s)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in s)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return hash;
        }
    }

    /// <summary>
    /// Строит имена параметров: {prefix}{hash8} и {prefix}{hash8}_s. Гарантии:
    /// длина ≤ 20 (varchar(20)), имена всегда различны — при нехватке места
    /// усечению подлежит ПРЕФИКС, суффиксы хеша и «_s» сохраняются.
    /// </summary>
    /// <remarks>
    /// Миграция: старые записи, сохранённые под рандомизированным хешем GetHashCode(),
    /// прочитаны быть не могут — они и так были нечитаемы после рестарта.
    /// Осиротевшие записи чистятся по префиксу StateParamPrefix в таблице параметров.
    /// </remarks>
    internal (string anchor, string sel) BuildParamNames(string treeId)
    {
        var hash = StableHash(treeId).ToString("X8"); // ровно 8 символов
        var prefix = _treeSettings.Value.StateParamPrefix;

        const int maxLen = 20;
        var selTail = hash + "_s";                       // 10 символов
        var maxPrefixLen = maxLen - selTail.Length;      // 10
        if (prefix.Length > maxPrefixLen)
            prefix = prefix[..maxPrefixLen];

        return (prefix + hash, prefix + selTail);
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
