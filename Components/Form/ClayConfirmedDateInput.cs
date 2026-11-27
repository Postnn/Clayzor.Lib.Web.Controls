using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Календарь с уведомлением после штатного подтверждения выбора.</summary>
public sealed class ClayConfirmedDateInput : MudDatePicker
{
    /// <summary>Дата после завершения штатного подтверждения.</summary>
    [Parameter] public EventCallback<DateTime?> Submitted { get; set; }

    /// <inheritdoc />
    protected override async Task SubmitAsync()
    {
        await base.SubmitAsync();
        await Submitted.InvokeAsync(Date);
    }
}
