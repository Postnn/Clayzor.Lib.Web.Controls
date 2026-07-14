> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G1 — ClayGridDynamicOptions + ClayGridSchemaMap + DI/appsettings

Прочитать перед началом: как в проекте регистрируются сервисы и читается конфигурация
(Program.cs приложения; любой существующий `AddClay*`/`services.Configure<>` в
Clayzor.Lib.Web.Controls).

Файлы создать (папка Components/Grid/Dynamic/):
- `ClayGridSchemaMap.cs` — имена колонок трёх таблиц. Свойства С# английские, значения по
  умолчанию — русские имена из спецификации:
```csharp
namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

public sealed class ClayGridSchemaMap
{
    public SettingsCols Settings { get; set; } = new();
    public ColumnCols Columns { get; set; } = new();
    public UserParamCols UserParams { get; set; } = new();

    public sealed class SettingsCols {
        public string GridId  { get; set; } = "КодЗапроса";
        public string Title   { get; set; } = "Запрос";
        public string Icon    { get; set; } = "Пиктограмма";
        public string Sql     { get; set; } = "SQL";
        public string Id      { get; set; } = "ID";
        public string IdName  { get; set; } = "IDName";
        public string EditForm{ get; set; } = "ФормаРедактирования";
        public string NewForm { get; set; } = "ФормаНового";
        public string SqlDelete{ get; set; } = "SQLDelete";
    }
    public sealed class ColumnCols {
        public string ColumnId { get; set; } = "КодКолонки";
        public string GridId   { get; set; } = "КодЗапроса";
        public string Column   { get; set; } = "Колонка";
        public string Header    { get; set; } = "ЗаголовокКолонки";
        public string UrlKey   { get; set; } = "КлючURL";
        public string Order     { get; set; } = "Порядок";
        public string Format    { get; set; } = "Формат";
        public string Type      { get; set; } = "Тип";
    }
    public sealed class UserParamCols {
        public string ClientId { get; set; } = "КодНастройкиКлиента";
        public string Name      { get; set; } = "Параметр";
        public string Value     { get; set; } = "Значение";
    }
}
```
- `ClayGridDynamicOptions.cs`:
```csharp
public sealed class ClayGridDynamicOptions
{
    public string ConnectionStringName { get; set; } = "";  // п.1
    public string SettingsTable        { get; set; } = "ClayGridSettings";     // п.2
    public string ColumnsTable         { get; set; } = "ClayGridColumns";      // п.3
    public string UserParamsTable      { get; set; } = "ClayGridUserParams";   // п.4
    public string GridIdQueryParam     { get; set; } = "id";    // п.5
    public string ColumnsParamPrefix   { get; set; } = "cols";  // п.6
    public string FilterParamPrefix    { get; set; } = "flt";   // п.7
    public string GroupingParamPrefix  { get; set; } = "grp";   // п.8
    public string SortingParamPrefix   { get; set; } = "srt";   // п.9
    public string PageSizeParamPrefix  { get; set; } = "pgs";   // п.10
    public string ClientIdQueryParam   { get; set; } = "CLID";
    public ClayGridSchemaMap Schema    { get; set; } = new();

    public void Validate()   // бросить с именем поля, если пусто
    {
        if (string.IsNullOrWhiteSpace(ConnectionStringName)) throw new InvalidOperationException("ClayGridDynamicOptions.ConnectionStringName пусто");
        if (string.IsNullOrWhiteSpace(SettingsTable))        throw new InvalidOperationException("ClayGridDynamicOptions.SettingsTable пусто");
        if (string.IsNullOrWhiteSpace(ColumnsTable))         throw new InvalidOperationException("ClayGridDynamicOptions.ColumnsTable пусто");
        if (string.IsNullOrWhiteSpace(UserParamsTable))      throw new InvalidOperationException("ClayGridDynamicOptions.UserParamsTable пусто");
    }
}
```
- DI-расширение `ServiceCollectionExtensions.AddClayGridDynamic(this IServiceCollection services,
  IConfiguration config, string section = "ClayGrid:Dynamic")`: `services.Configure<ClayGridDynamicOptions>(config.GetSection(section))`,
  плюс валидация при старте (например через `IValidateOptions` или явный `options.Validate()` в
  фабрике). Зарегистрируй так же будущие репозитории (заглушки добавишь в G2/G6).

Изменить: appsettings.json приложения — добавь секцию `"ClayGrid": { "Dynamic": { "ConnectionStringName": "...", ... } }`;
в Program.cs вызови `builder.Services.AddClayGridDynamic(builder.Configuration);`.

Не делай: не читай строку подключения напрямую из appsettings в опциях — только имя; резолвинг
самой строки — в репозитории через существующий механизм.

Проверка (юнит-тест, см. TG1):
- байнд из in-memory конфигурации `{"ClayGrid:Dynamic:ConnectionStringName":"Main", ... }`
  заполняет все поля; `Schema.Settings.Title` по умолчанию == "Запрос";
- `Validate()` при пустом `ConnectionStringName` бросает исключение с текстом, содержащим
  "ConnectionStringName".
