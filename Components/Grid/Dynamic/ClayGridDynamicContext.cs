namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>Контекст списка от вызывающей страницы. Остаётся активным при очистке пользовательских фильтров.</summary>
/// <param name="ClientId">Клиентский контекст пользовательских настроек.</param>
/// <param name="Column">Имя проверяемой колонки определения грида.</param>
/// <param name="Value">Идентификатор выбранной родительской записи.</param>
/// <param name="CreateParameter">Имя параметра формы создания, которому передаётся выбранное значение.</param>
public sealed record ClayGridDynamicContext(int ClientId, string Column, int Value, string CreateParameter);
