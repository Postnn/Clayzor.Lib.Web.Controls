namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Элемент списка в диалоге настройки колонок ClayGrid.
/// </summary>
public class ColumnSettingsItem
{
    /// <summary>SQL-имя колонки (выходное имя из SELECT).</summary>
    public string SqlName { get; init; } = "";

    /// <summary>Отображаемое имя.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Видимость колонки в гриде.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Переключатель заблокирован (колонка в группировке).</summary>
    public bool IsReadonly { get; set; }

    /// <summary>
    /// Приоритет сортировки: 0 — не сортируется, 1 — высший, 2 — второй.
    /// Максимум 2 колонки в сортировке.
    /// </summary>
    public int SortPriority { get; set; }

    /// <summary>
    /// Направление сортировки: <c>false</c> — по возрастанию, <c>true</c> — по убыванию.
    /// Имеет значение только при <see cref="SortPriority"/> > 0.
    /// </summary>
    public bool IsSortDesc { get; set; }

    /// <summary>
    /// Приоритет группировки: 0 — не группируется, 1 — внешний уровень, 2 — следующий и т.д.
    /// Ограничения на число уровней нет (см. план GN).
    /// </summary>
    public int GroupPriority { get; set; }

    /// <summary>Разрешена ли группировка по колонке (ClayColumnMeta.Groupable).</summary>
    public bool Groupable { get; init; }

    /// <summary>
    /// Разрешён ли фильтр по уникальным значениям (Excel-style) для колонки.
    /// Заполняется из <see cref="ClayColumnMeta.AllowValueFilter"/>,
    /// пользователь может переключить в диалоге настройки.
    /// </summary>
    public bool AllowValueFilter { get; set; }

    /// <summary>Участвует в быстром поиске (QS6).</summary>
    public bool QuickSearch { get; set; }

    /// <summary>Чекбокс быстрого поиска заблокирован (недопустимый тип, QS4).</summary>
    public bool QuickSearchDisabled { get; set; }

    /// <summary>Админское значение по умолчанию из ClayGridColumns (для кнопки сброса).</summary>
    public bool QuickSearchDefault { get; set; }
}
