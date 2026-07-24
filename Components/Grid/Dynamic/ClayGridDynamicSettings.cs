using Clayzor.Lib.Entities.DynamicGrid;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Настройки динамического режима ClayGrid уровня приложения, связываемые из секции
/// "ClayGrid:Dynamic" и живущие в DI. В отличие от <see cref="Grid.ClayGridOptions"/> —
/// настроек одного экземпляра грида на конкретной странице.
/// </summary>
public sealed class ClayGridDynamicSettings
{
    /// <summary>Имя строки подключения (из ConnectionStrings). Сама строка здесь не хранится.</summary>
    public string ConnectionStringName { get; set; } = "";

    /// <summary>Имя таблицы настроек грида. По умолчанию: "ClayGridSettings".</summary>
    public string SettingsTable { get; set; } = "ClayGridSettings";

    /// <summary>Имя таблицы настроек колонок. По умолчанию: "ClayGridColumns".</summary>
    public string ColumnsTable { get; set; } = "ClayGridColumns";

    /// <summary>Имя таблицы параметров пользователя. По умолчанию: "ClayGridUserParams".</summary>
    public string UserParamsTable { get; set; } = "ClayGridUserParams";

    /// <summary>Имя query-параметра для ID грида. По умолчанию: "id".</summary>
    public string GridIdQueryParam { get; set; } = "id";

    /// <summary>Префикс параметра видимости/порядка колонок. По умолчанию: "cols".</summary>
    public string ColumnsParamPrefix { get; set; } = "cols";

    /// <summary>Префикс параметра фильтра. По умолчанию: "flt".</summary>
    public string FilterParamPrefix { get; set; } = "flt";

    /// <summary>Префикс параметра группировки. По умолчанию: "grp".</summary>
    public string GroupingParamPrefix { get; set; } = "grp";

    /// <summary>Префикс параметра сортировки. По умолчанию: "srt".</summary>
    public string SortingParamPrefix { get; set; } = "srt";

    /// <summary>Префикс параметра размера страницы. По умолчанию: "pgs".</summary>
    public string PageSizeParamPrefix { get; set; } = "pgs";

    /// <summary>Префикс параметра быстрого поиска. По умолчанию: "qks".</summary>
    public string QuickSearchParamPrefix { get; set; } = "qks";

    /// <summary>Имя query-параметра для CLID (идентификатор клиента). По умолчанию: "CLID".</summary>
    public string ClientIdQueryParam { get; set; } = "CLID";

    /// <summary>Маппинг имён колонок БД → свойства C#.</summary>
    public ClayGridSchemaMap Schema { get; set; } = new();

    /// <summary>
    /// Проверяет заполненность обязательных полей. Бросает <see cref="InvalidOperationException"/>
    /// с именем поля, если поле пустое.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionStringName))
            throw new InvalidOperationException("ClayGridDynamicSettings.ConnectionStringName пусто");
        if (string.IsNullOrWhiteSpace(SettingsTable))
            throw new InvalidOperationException("ClayGridDynamicSettings.SettingsTable пусто");
        if (string.IsNullOrWhiteSpace(ColumnsTable))
            throw new InvalidOperationException("ClayGridDynamicSettings.ColumnsTable пусто");
        if (string.IsNullOrWhiteSpace(UserParamsTable))
            throw new InvalidOperationException("ClayGridDynamicSettings.UserParamsTable пусто");
    }
}
