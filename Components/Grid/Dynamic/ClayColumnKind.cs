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
