using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Часы с уведомлением после штатного подтверждения выбора.</summary>
public sealed class ClayConfirmedTimeInput : MudTimePicker
{
    /// <summary>Время после завершения штатного подтверждения.</summary>
    [Parameter] public EventCallback<TimeSpan?> Submitted { get; set; }

    /// <inheritdoc />
    protected override async Task SubmitAsync()
    {
        await base.SubmitAsync();
        await Submitted.InvokeAsync(Time);
    }
}
