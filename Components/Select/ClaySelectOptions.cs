namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Настройки одного экземпляра <see cref="ClaySelect{TItem, TValue}"/> на странице.
/// Объект создаётся страницей один раз и не собирается выражением в разметке.
/// </summary>
public sealed class ClaySelectOptions
{
    /// <summary>
    /// Значения по умолчанию. Экземпляр общий и не должен изменяться.
    /// </summary>
    public static ClaySelectOptions Defaults { get; } = new();

    /// <summary>
    /// Максимальное количество видимых результатов поиска.
    /// </summary>
    public int MaxItems { get; set; } = 8;

    /// <summary>
    /// Задержка перед поиском после последнего изменения текста в миллисекундах.
    /// </summary>
    public int DebounceInterval { get; set; } = 300;

    /// <summary>
    /// Минимальная длина текста для поиска. Значение 0 разрешает поиск с пустой строкой.
    /// </summary>
    public int MinSearchLength { get; set; }

    /// <summary>
    /// Автоматически выбирать элемент, если поиск вернул ровно один результат.
    /// </summary>
    public bool AutoSelectSingle { get; set; }

    /// <summary>
    /// Автоматически выбирать элемент при точном совпадении текста без учёта регистра.
    /// </summary>
    public bool AutoSelectExactMatch { get; set; } = true;

    /// <summary>
    /// Разрешить очистку выбранного значения.
    /// </summary>
    public bool Clearable { get; set; } = true;

    /// <summary>
    /// Подпись поля.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Подсказка в пустом поле.
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// Признак обязательного выбора значения.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Текст ошибки обязательного поля.
    /// </summary>
    public string RequiredError { get; set; } = "Выберите значение";

    /// <summary>
    /// Отключает взаимодействие с компонентом.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Запрещает изменение значения, сохраняя поле доступным для чтения.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Показывать расширенный поиск всегда, а не только при отсутствии или избытке результатов.
    /// </summary>
    public bool AlwaysShowAdvancedSearch { get; set; }

    /// <summary>
    /// Дополнительные CSS-классы корневого контейнера.
    /// </summary>
    public string? Class { get; set; }

    /// <summary>
    /// Текст состояния без результатов.
    /// </summary>
    public string NoResultsText { get; set; } = "Ничего не найдено";

    /// <summary>
    /// Текст предложения уточнить запрос.
    /// </summary>
    public string RefineSearchText { get; set; } = "Уточните поиск";

    /// <summary>
    /// Текст признака дополнительных результатов.
    /// </summary>
    public string MoreResultsText { get; set; } = "Есть ещё результаты. Уточните поиск";

    /// <summary>
    /// Текст пункта расширенного поиска.
    /// </summary>
    public string AdvancedSearchText { get; set; } = "Расширенный поиск";

    /// <summary>
    /// Текст индикатора загрузки результатов.
    /// </summary>
    public string LoadingText { get; set; } = "Загрузка...";

    /// <summary>
    /// Доступное название кнопки открытия списка.
    /// </summary>
    public string OpenListAriaLabel { get; set; } = "Открыть список";
}
