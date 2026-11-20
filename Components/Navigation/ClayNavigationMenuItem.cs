namespace Clayzor.Lib.Web.Controls.Components.Navigation;

/// <summary>Типизированный пункт универсального навигационного меню.</summary>
public sealed class ClayNavigationMenuItem
{
    /// <summary>Стабильный идентификатор пункта в пределах переданного дерева.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Отображаемый текст.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>URL перехода. Пункты с дочерними элементами используют его только при отсутствии Children.</summary>
    public string? Href { get; init; }

    /// <summary>URL пиктограммы. Null отображает нейтральный fallback.</summary>
    public string? IconUrl { get; init; }

    /// <summary>Подсказка с полным текстом или пояснением недоступности.</summary>
    public string? Title { get; init; }

    /// <summary>Запрещает активацию пункта.</summary>
    public bool Disabled { get; init; }

    /// <summary>Признак выбранного пункта.</summary>
    public bool IsActive { get; init; }

    /// <summary>Добавляет разделитель перед пунктом.</summary>
    public bool SeparatorBefore { get; init; }

    /// <summary>Дочерние пункты произвольной глубины.</summary>
    public IReadOnlyList<ClayNavigationMenuItem> Children { get; init; } = [];

    /// <summary>Дополнительное действие пункта без URL.</summary>
    public Func<Task>? OnActivate { get; init; }
}
