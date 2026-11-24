namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>Обязательное чтение ClaySelect, завершившееся ожидаемой ошибкой источника.</summary>
public enum ClaySelectReadOperation
{
    /// <summary>Поиск по введённому тексту.</summary>
    Search,

    /// <summary>Восстановление объекта по сохранённому ID.</summary>
    Resolve,
}

/// <summary>Контекст ошибки чтения; исходный Exception доступен владельцу для диагностики.</summary>
public sealed record ClaySelectReadError(
    ClaySelectReadOperation Operation,
    Exception Exception,
    string? SearchText);
