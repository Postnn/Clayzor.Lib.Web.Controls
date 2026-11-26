namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Контекст вызова расширенного поиска из <see cref="ClaySelect{TItem, TValue}"/>.
/// </summary>
public sealed class ClaySelectAdvancedSearchContext
{
    /// <summary>
    /// Текущий поисковый текст, который можно передать в открываемую форму.
    /// </summary>
    public string SearchText { get; init; } = string.Empty;

    internal Delegate? ApplyResult { get; init; }

    /// <summary>Применяет результат к исходному экземпляру ClaySelect и подтверждает актуальный выбор.</summary>
    public Task<ClaySelectReturnDecision> ApplyAsync<TItem, TValue>(
        ClaySelectExternalResult<TItem, TValue> result, CancellationToken cancellationToken = default)
        where TItem : class =>
        ApplyResult is Func<ClaySelectExternalResult<TItem, TValue>, CancellationToken, Task<ClaySelectReturnDecision>> apply
            ? apply(result, cancellationToken)
            : Task.FromResult(ClaySelectReturnDecision.Stale);
}
