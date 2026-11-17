using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Открывает формы редактирования в отдельных окнах с сохранёнными параметрами.</summary>
public class ClayFormWindowService
{
    private readonly IJSRuntime _js;

    /// <summary>Создаёт сервис окон текущего Blazor circuit.</summary>
    public ClayFormWindowService(IJSRuntime js) => _js = js;

    /// <summary>
    /// Получает параметры окна. Пока это fake-вызов с параметрами по умолчанию;
    /// впоследствии здесь будет обращение к веб-сервису пользовательских настроек.
    /// </summary>
    public virtual Task<ClayFormWindowOptions> GetOptionsAsync(
        string formKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ClayFormWindowOptions());

    /// <summary>Получает параметры и открывает форму без навигации родительской страницы.</summary>
    public async Task<bool> OpenAsync(
        string url,
        string formKey,
        CancellationToken cancellationToken = default)
    {
        var options = await GetOptionsAsync(formKey, cancellationToken);
        return await _js.InvokeAsync<bool>("clayFormWindow.open", cancellationToken, url, formKey, options);
    }
}
