namespace Clayzor.Lib.Web.Controls.Components.Filter;

/// <summary>
/// Вариант значения для выпадающего списка в диалоге фильтра.
/// </summary>
public sealed class ClayFilterOption
{
    /// <summary>Значение (строка или число), уходящее в SQL-параметр.</summary>
    public object? Value { get; set; }
    /// <summary>Отображаемая метка.</summary>
    public string Label { get; set; } = "";
}
