namespace Clayzor.Lib.Web.Controls.Components.Navigation;

/// <summary>Настройки одного экземпляра универсального навигационного меню.</summary>
public sealed class ClayNavigationMenuOptions
{
    /// <summary>Доступное имя навигации для assistive technologies.</summary>
    public string AriaLabel { get; set; } = "Навигация";

    /// <summary>Задержка открытия и закрытия при наведении мыши в миллисекундах.</summary>
    public int HoverDelayMs { get; set; } = 300;

    /// <summary>Безопасный отступ popup от границ viewport в пикселях.</summary>
    public int ViewportMarginPx { get; set; } = 8;

    /// <summary>Максимальная ширина popup в пикселях.</summary>
    public int MaxPanelWidthPx { get; set; } = 360;
}
