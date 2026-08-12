namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Value-based identity динамического режима ClayGrid (CGFR1).
/// Смена любой компоненты — полная инвалидация старого dynamic runtime.
/// Record struct даёт value-based equality по всем полям (string — Ordinal).
/// </summary>
internal readonly record struct ClayGridDynamicKey(
    int GridId,
    int Clid,
    int? SharedId,
    string ConnectionStringName,
    string SettingsTable,
    string ColumnsTable,
    string UserParamsTable,
    string UserSharedParamsTable,
    string UserParamsShared,
    string GridIdQueryParam,
    string ClientIdQueryParam,
    string ColumnsParamPrefix,
    string FilterParamPrefix,
    string GroupingParamPrefix,
    string SortingParamPrefix,
    string PageSizeParamPrefix,
    string QuickSearchParamPrefix)
{
    /// <summary>
    /// Строит ключ из разрешённых identity-значений и application-level dynamic settings (CGFR1 §4).
    /// Presentation-only поля <see cref="ClayGridOptions"/> (Title, ShowAddButton, ...) не участвуют.
    /// <see cref="ClayGridDynamicSettings.Schema"/> исключён — bound once через DI.
    /// </summary>
    public static ClayGridDynamicKey Create(int gridId, int clid, int? sharedId, ClayGridDynamicSettings s)
        => new(
            gridId, clid, sharedId,
            s.ConnectionStringName,
            s.SettingsTable, s.ColumnsTable, s.UserParamsTable, s.UserSharedParamsTable, s.UserParamsShared,
            s.GridIdQueryParam, s.ClientIdQueryParam,
            s.ColumnsParamPrefix, s.FilterParamPrefix, s.GroupingParamPrefix,
            s.SortingParamPrefix, s.PageSizeParamPrefix, s.QuickSearchParamPrefix);
}
