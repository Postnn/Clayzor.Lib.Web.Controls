using System.Collections.Specialized;
using System.Web;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Построитель URL для «Поделиться». Чистая функция — тестируется без браузера и DI.
/// </summary>
public static class ClayShareUrlBuilder
{
    /// <summary>
    /// Имя query-параметра sharedId. Пока константа (подтверждение заказчика — SH0, решение 8).
    /// </summary>
    public const string SharedIdParam = "sharedId";

    /// <summary>
    /// Строит URL для «Поделиться» из текущего URL страницы.
    /// Оставляет только идентификатор грида, добавляет sharedId.
    /// Все прочие параметры (фильтры, страница, сортировка, фрагмент) отбрасываются.
    /// </summary>
    /// <param name="currentUrl">Текущий абсолютный URL страницы (NavigationManager.Uri).</param>
    /// <param name="gridIdParam">Имя query-параметра идентификатора грида.</param>
    /// <param name="sharedId">Значение sharedId (КодНастройкиОбщей).</param>
    /// <returns>Абсолютный URL с двумя параметрами: gridId и sharedId.</returns>
    public static string BuildShareUrl(string currentUrl, string gridIdParam, int sharedId)
    {
        var uri = new Uri(currentUrl);
        var baseUrl = uri.GetLeftPart(UriPartial.Path);

        // Белый список: оставляем только gridId, всё остальное выбрасываем
        var currentParams = HttpUtility.ParseQueryString(uri.Query);
        var filtered = new List<(string Key, string? Value)>();

        var gridIdValue = currentParams[gridIdParam];
        if (gridIdValue is not null)
            filtered.Add((gridIdParam, gridIdValue));

        // Добавляем sharedId (заменяет, если уже был)
        filtered.Add((SharedIdParam, sharedId.ToString()));

        // Собираем query-строку с URL-кодированием
        var queryParts = filtered
            .Select(p => $"{HttpUtility.UrlEncode(p.Key)}={HttpUtility.UrlEncode(p.Value)}");
        var query = string.Join("&", queryParts);

        return query.Length > 0 ? $"{baseUrl}?{query}" : baseUrl;
    }
}
