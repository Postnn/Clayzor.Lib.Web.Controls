namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Имена колонок трёх таблиц динамического грида.
/// Свойства C# английские, значения по умолчанию — русские имена из спецификации.
/// При несовпадении с реальной схемой БД — переопределяется в appsettings.json
/// (секция ClayGrid:Dynamic:Schema).
/// </summary>
public sealed class ClayGridSchemaMap
{
    /// <summary>Колонки таблицы настроек грида (ClayGridSettings).</summary>
    public SettingsCols Settings { get; set; } = new();

    /// <summary>Колонки таблицы настроек колонок (ClayGridColumns).</summary>
    public ColumnCols Columns { get; set; } = new();

    /// <summary>Колонки таблицы параметров пользователя (ClayGridUserParams).</summary>
    public UserParamCols UserParams { get; set; } = new();

    /// <summary>Имена колонок таблицы ClayGridSettings.</summary>
    public sealed class SettingsCols
    {
        public string GridId   { get; set; } = "КодЗапроса";
        public string Title    { get; set; } = "Запрос";
        public string Icon     { get; set; } = "Пиктограмма";
        public string Sql      { get; set; } = "SQL";
        public string Id       { get; set; } = "ID";
        public string IdName   { get; set; } = "IDName";
        public string EditForm { get; set; } = "ФормаРедактирования";
        public string NewForm  { get; set; } = "ФормаНового";
        public string SqlDelete{ get; set; } = "SQLDelete";
    }

    /// <summary>Имена колонок таблицы ClayGridColumns.</summary>
    public sealed class ColumnCols
    {
        public string ColumnId { get; set; } = "КодКолонки";
        public string GridId   { get; set; } = "КодЗапроса";
        public string Column   { get; set; } = "Колонка";
        public string Header   { get; set; } = "ЗаголовокКолонки";
        public string UrlKey   { get; set; } = "КлючURL";
        public string Order    { get; set; } = "Порядок";
        public string Format   { get; set; } = "Формат";
        public string Type     { get; set; } = "Тип";
    }

    /// <summary>Имена колонок таблицы ClayGridUserParams.</summary>
    public sealed class UserParamCols
    {
        public string ClientId { get; set; } = "КодНастройкиКлиента";
        public string Name     { get; set; } = "Параметр";
        public string Value    { get; set; } = "Значение";
    }
}
