namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Запрос на экспорт данных грида в Excel.
/// Передаётся от <see cref="ClayGrid{TEntity}"/> к <see cref="IClayGridDataLoader"/>.
/// </summary>
public sealed record ExcelExportRequest
{
    /// <summary>Режим выгрузки: текущая страница, выбранные записи, все данные.</summary>
    public ExcelExportMode Mode { get; init; }

    /// <summary>Заголовок грида — первая строка Excel.</summary>
    public string Title { get; init; } = "";

    /// <summary>Отображаемые колонки в порядке отображения (без скрытых и сгруппированных).</summary>
    public IReadOnlyList<ClayColumnMeta> VisibleColumns { get; init; } = [];

    /// <summary>Текстовое представление активных фильтров (или null).</summary>
    public string? FilterDescription { get; init; }

    /// <summary>Текстовое представление колонок группировки (или null).</summary>
    public string? GroupDescription { get; init; }

    /// <summary>
    /// ID выбранных сущностей для режима <see cref="ExcelExportMode.Selected"/>.
    /// Заполняется гридом из <see cref="ClayGrid{TEntity}.SelectedIds"/>.
    /// </summary>
    public IReadOnlyList<int> SelectedIds { get; init; } = [];
}

/// <summary>Режим выгрузки в Excel.</summary>
public enum ExcelExportMode
{
    /// <summary>Только данные текущей страницы.</summary>
    CurrentPage,

    /// <summary>Только выбранные записи.</summary>
    Selected,

    /// <summary>Все данные по текущему запросу.</summary>
    All
}
