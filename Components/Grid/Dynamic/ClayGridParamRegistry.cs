using Clayzor.Lib.Entities.DynamicGrid;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Единый реестр имён параметров динамического грида.
/// Единственный источник истины для: копирования в общую ссылку (SH5),
/// поиска общих настроек грида (SH7) и проверки соответствия при открытии по sharedId (SH8).
/// </summary>
public static class ClayGridParamRegistry
{
    /// <summary>
    /// Возвращает полный набор имён параметров (ClayGridUserParams.Параметр),
    /// относящихся к указанному гриду. Каждое имя = префикс из настроек + gridId.
    /// Настройки передаются явно — метод остаётся чистой функцией и тестируется без DI.
    /// </summary>
    /// <param name="settings">Действующие настройки динамического режима.</param>
    /// <param name="gridId">Идентификатор запроса (КодЗапроса).</param>
    /// <returns>6 имён параметров: columns, filter, grouping, sorting, pageSize, quickSearch.</returns>
    public static IReadOnlyList<string> GetGridParamNames(ClayGridDynamicSettings settings, int gridId)
    {
        return new[]
        {
            BuildWithCheck(settings.ColumnsParamPrefix,     nameof(settings.ColumnsParamPrefix),     gridId),
            BuildWithCheck(settings.FilterParamPrefix,      nameof(settings.FilterParamPrefix),      gridId),
            BuildWithCheck(settings.GroupingParamPrefix,    nameof(settings.GroupingParamPrefix),    gridId),
            BuildWithCheck(settings.SortingParamPrefix,     nameof(settings.SortingParamPrefix),     gridId),
            BuildWithCheck(settings.PageSizeParamPrefix,    nameof(settings.PageSizeParamPrefix),    gridId),
            BuildWithCheck(settings.QuickSearchParamPrefix, nameof(settings.QuickSearchParamPrefix), gridId),
        };
    }

    /// <summary>
    /// Строит имя параметра через <see cref="ClayGridUserParamsData.BuildParamName"/>
    /// и при превышении 20 символов оборачивает ошибку с указанием свойства-префикса.
    /// </summary>
    private static string BuildWithCheck(string prefix, string propertyName, int gridId)
    {
        try
        {
            return ClayGridUserParamsData.BuildParamName(prefix, gridId);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Имя параметра с префиксом \"{prefix}\" (свойство ClayGridDynamicSettings.{propertyName}) " +
                $"не укладывается в 20 символов для gridId={gridId}. " +
                $"Уменьшите префикс или идентификатор запроса.");
        }
    }
}
