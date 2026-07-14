using Microsoft.Extensions.Configuration;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Резолвинг ссылок из определения динамического грида.
/// Поддерживает прямые URL, null/пусто (нет действия) и ссылки на конфигурацию (@Key).
/// </summary>
public static class ClayGridLinkResolver
{
    /// <summary>
    /// Резолвит значение ссылки:
    /// <list type="bullet">
    ///   <item>null или пусто → null (нет действия).</item>
    ///   <item>Начинается с '@' → значение из <paramref name="config"/> по ключу без '@'.
    ///         Если не найдено — null.</item>
    ///   <item>Иначе → возвращает как есть (относительный или абсолютный URL).</item>
    /// </list>
    /// </summary>
    /// <param name="value">Сырое значение из БД.</param>
    /// <param name="config">Конфигурация для резолвинга '@'-ссылок (опционально).</param>
    /// <returns>URL или null, если действие недоступно.</returns>
    public static string? Resolve(string? value, IConfiguration? config = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.StartsWith('@'))
        {
            var key = value[1..];
            return config?[key];
        }

        return value;
    }
}
