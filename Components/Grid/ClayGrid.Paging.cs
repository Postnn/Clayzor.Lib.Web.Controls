namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>Текущий номер страницы.</summary>
    private int _pageNumber = 1;

    /// <summary>Текущий размер страницы.</summary>
    private int _pageSize;

    /// <summary>Общее число страниц на основе <see cref="TotalCount"/> и <see cref="_pageSize"/>.</summary>
    private int _totalPages => _pageSize > 0 && TotalCount > 0
        ? (int)Math.Ceiling((double)TotalCount / _pageSize)
        : 1;

    private async Task OnPageSizeChangedAsync(int value)
    {
        _pageSize = value;
        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    /// <summary>
    /// Сбрасывает номер страницы на 1 и инициирует перезагрузку данных.
    /// </summary>
    public async Task RefreshAsync()
    {
        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    private async Task GoToFirstPageAsync()
    {
        if (_pageNumber <= 1) return;
        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    private async Task GoToPrevPageAsync()
    {
        if (_pageNumber <= 1) return;
        _pageNumber--;
        await NotifyQueryChanged();
    }

    private async Task GoToNextPageAsync()
    {
        if (_pageNumber >= _totalPages) return;
        _pageNumber++;
        await NotifyQueryChanged();
    }

    private async Task GoToLastPageAsync()
    {
        if (_pageNumber >= _totalPages) return;
        _pageNumber = _totalPages;
        await NotifyQueryChanged();
    }
}
