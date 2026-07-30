namespace Clayzor.Lib.Web.Controls.Components.Filter;

/// <summary>
/// Источник происхождения условия фильтра.
/// </summary>
public enum ClayFilterSource
{
    /// <summary>Создано через диалог отдельной колонки.</summary>
    ColumnDialog,
    /// <summary>Создано через диалог настраиваемого фильтра (составной фильтр).</summary>
    CompositeDialog,
    /// <summary>Создано через диалог фильтра по уникальному значению.</summary>
    ValueFilter,
}
