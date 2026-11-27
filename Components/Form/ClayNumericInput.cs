using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Числовой ввод с явной проверкой перед переходом по Enter.</summary>
public sealed class ClayNumericInput : MudNumericField<decimal?>
{
    private string? _enterError;

    /// <summary>Использует проверку диапазона числового поля для обычных изменений и Enter.</summary>
    public ClayNumericInput()
    {
        Validation = new Func<decimal?, Task<string?>>(ValidateNumberAsync);
    }

    internal async Task<(bool Accepted, decimal? Value)> CommitEnterValueAsync(string text)
    {
        var value = ConvertGet(text);
        _enterError = Required && value is null && !ConversionError
            ? ResolvedRequiredErrorText()
            : ConstrainBoundaries(value).changed ? "Число вне допустимого диапазона." : null;
        if (_enterError is null && !ConversionError)
        {
            await SetTextCoreAsync(text);
            await SetValueAndUpdateTextAsync(value, updateText: false);
        }
        await ValidateAsync();
        return (!HasErrors && ReadValue == value, value);
    }

    private async Task<string?> ValidateNumberAsync(decimal? value)
    {
        if (_enterError is not null) return _enterError;
        await ValidateInput(value);
        return null;
    }

    /// <inheritdoc />
    protected override Task SetTextAndUpdateValueAsync(string? text, bool updateValue = true)
    {
        if (text != ReadText) _enterError = null;
        return base.SetTextAndUpdateValueAsync(text, updateValue);
    }

    /// <inheritdoc />
    protected override Task OnBlurredAsync(FocusEventArgs args)
    {
        _enterError = null;
        return base.OnBlurredAsync(args);
    }
}
