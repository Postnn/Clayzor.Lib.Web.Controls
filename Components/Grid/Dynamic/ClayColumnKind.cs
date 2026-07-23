namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Типы колонок динамического грида (1–13).
/// Соответствует значениям колонки <c>Тип</c> в таблице ClayGridColumns.
/// </summary>
public enum ClayColumnKind
{
    /// <summary>Число (int/long/decimal).</summary>
    Number = 1,

    /// <summary>Текст (строка).</summary>
    Text = 2,

    /// <summary>Дата (DateTime/DateOnly).</summary>
    Date = 3,

    /// <summary>Ссылка (HTML-гиперссылка).</summary>
    Link = 4,

    /// <summary>Список (подзапрос-справочник).</summary>
    List = 5,

    /// <summary>Условие булево (фильтр-онли).</summary>
    ConditionBool = 6,

    /// <summary>Булево (чекбокс).</summary>
    Bool = 7,

    /// <summary>HTML (санитизированный вывод).</summary>
    Html = 8,

    /// <summary>Пиктограмма (3-колоночный подзапрос).</summary>
    Icon = 9,

    /// <summary>Дата-время локализованное (UTC→клиент).</summary>
    DateTimeLocal = 10,

    /// <summary>Условие список (фильтр-онли, выбор из whereExpr).</summary>
    ConditionList = 11,

    /// <summary>Текст с ограничением длины (обрезка + «…»).</summary>
    LimitedText = 12,

    /// <summary>Время локализованное (UTC→клиент).</summary>
    TimeLocal = 13
}

/// <summary>
/// Вспомогательные методы для <see cref="ClayColumnKind"/>.
/// </summary>
public static class ClayColumnKindExtensions
{
    /// <summary>
    /// Определяет, может ли колонка с указанным <see cref="ClayColumnKind"/> участвовать
    /// в быстром поиске. Допустимы только типы, значение которых берётся напрямую
    /// из исходного запроса грида (строки, числа, даты, ссылки).
    /// Справочные (List, Icon) и вычисляемые (Bool, Html) типы исключены:
    /// отображаемый текст формируется после выборки и не может быть найден
    /// условием WHERE по источнику.
    /// Приоритет: недопустимый тип перебивает флаг УчаствуетВБыстромПоиске=1.
    /// </summary>
    /// <param name="kind">Код типа колонки (1–13).</param>
    /// <returns>true — колонка может участвовать в быстром поиске.</returns>
    public static bool SupportsQuickSearch(int kind) => kind switch
    {
        (int)ClayColumnKind.Number        => true,
        (int)ClayColumnKind.Text          => true,
        (int)ClayColumnKind.Date          => true,
        (int)ClayColumnKind.Link          => true,
        (int)ClayColumnKind.DateTimeLocal => true,
        (int)ClayColumnKind.LimitedText   => true,
        (int)ClayColumnKind.TimeLocal     => true,
        _ => false
    };
}
