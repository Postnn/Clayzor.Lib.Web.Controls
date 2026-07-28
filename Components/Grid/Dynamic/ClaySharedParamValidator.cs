namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Проверка соответствия имён параметров общей настройки текущему гриду.
/// Чистые функции — тестируются без БД.
/// </summary>
public static class ClaySharedParamValidator
{
    /// <summary>
    /// Проверяет, что все имена параметров из shared-набора принадлежат текущему гриду.
    /// Подмножество допустимо: пользователь мог не менять часть настроек.
    /// Хотя бы одно незнакомое имя → несоответствие (строгая проверка).
    /// Сравнение — <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    /// <param name="sharedParamNames">Имена параметров, полученные из UserParamsShared.</param>
    /// <param name="gridParamNames">Имена параметров текущего грида (из реестра SH4).</param>
    /// <returns><c>true</c> — все имена принадлежат гриду; <c>false</c> — есть чужое.</returns>
    public static bool IsValid(
        IEnumerable<string> sharedParamNames,
        IReadOnlyList<string> gridParamNames)
    {
        var known = new HashSet<string>(gridParamNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in sharedParamNames)
        {
            if (!known.Contains(name))
                return false;
        }
        return true;
    }
}
