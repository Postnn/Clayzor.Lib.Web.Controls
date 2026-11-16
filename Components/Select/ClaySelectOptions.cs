namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Настройки одного экземпляра <see cref="ClaySelect{TItem, TValue}"/> на странице.
/// Объект создаётся страницей один раз и не собирается выражением в разметке.
/// </summary>
public sealed class ClaySelectOptions
{
    /// <summary>
    /// Максимальное количество видимых результатов поиска. Должно быть больше нуля.
    /// Недопустимое значение вызывает <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public int MaxItems { get; set; } = 8;

    /// <summary>
    /// Неотрицательная задержка перед поиском после последнего изменения текста в миллисекундах.
    /// </summary>
    public int DebounceInterval { get; set; } = 300;

    /// <summary>
    /// Неотрицательная минимальная длина текста для поиска. Значение 0 разрешает пустой запрос.
    /// </summary>
    public int MinSearchLength { get; set; }

    /// <summary>
    /// Автоматически выбирать элемент после ввода непустого текста, если получен ровно один
    /// результат и <c>HasMore=false</c>. При открытии начального списка автовыбор не выполняется.
    /// </summary>
    public bool AutoSelectSingle { get; set; }

    /// <summary>
    /// Автоматически выбирать после ввода непустого текста единственный элемент с точным
    /// совпадением названия без учёта регистра. При <c>HasMore=true</c> автовыбор запрещён,
    /// поскольку уникальность совпадения не доказана. Начальный список автовыбор не выполняет.
    /// </summary>
    public bool AutoSelectExactMatch { get; set; } = true;

    /// <summary>
    /// Разрешить очистку выбранного значения. Очистка недоступна в Disabled и ReadOnly состояниях.
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
    /// Признак обязательного выбора значения. Проверяется именно выбранный ID,
    /// а не наличие поискового текста. Компонент интегрируется с MudForm.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Текст ошибки обязательного поля.
    /// </summary>
    public string RequiredError { get; set; } = "Выберите значение";

    /// <summary>
    /// Отключает взаимодействие с компонентом и все способы изменения значения.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Запрещает изменение значения всеми способами, сохраняя поле доступным для чтения.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Показывать заданный расширенный поиск всегда, а не только при отсутствии
    /// или избытке результатов. Без обработчика AdvancedSearch пункт не отображается.
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
