namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>Текущий текст поиска.</summary>
    private string? _searchText;

    /// <summary>
    /// Обработчик изменения строки поиска.
    /// Сбрасывает страницу на первую и инициирует перезагрузку данных.
    /// </summary>
    private async Task OnSearchTextChanged(string? value)
    {
        _searchText = value;
        _pageNumber = 1;
        await NotifyQueryChanged();
    }
}
