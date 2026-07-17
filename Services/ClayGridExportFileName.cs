namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Имя файла выгрузки: убирает символы, недопустимые в имени файла.</summary>
public static class ClayGridExportFileName
{
    /// <summary>Заменяет недопустимые символы. Пустой заголовок → «Данные».</summary>
    public static string Sanitize(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Данные";
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", title.Split(invalid, StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('.');
    }
}
