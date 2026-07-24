using Clayzor.Lib.Web.Controls.Components.Grid.Filter;
using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Метаданные колонки, зарегистрированной через <see cref="ClayColumnDef"/>.
/// </summary>
public sealed class ClayColumnMeta
{
    /// <summary>
    /// Числовой идентификатор колонки — связь <see cref="ClayColumnDef"/> ↔ <see cref="ClayColumn{TEntity}"/>.
    /// </summary>
    public int ColumnId { get; init; }

    /// <summary>SQL-имя колонки — выходное имя из SELECT. Идентификатор для группировки, фильтрации, drag.</summary>
    public string SqlName { get; init; } = "";

    /// <summary>Отображаемое имя для заголовков и треев.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// Имя колонки для ORDER BY. Если не задано при регистрации, равно <see cref="SqlName"/>.
    /// </summary>
    public string SortName { get; init; } = "";

    /// <summary>Разрешена ли группировка по колонке.</summary>
    public bool Groupable { get; init; }

    /// <summary>Разрешена ли фильтрация по колонке.</summary>
    public bool Filterable { get; init; }

    /// <summary>Разрешена ли фильтрация по набору значений (Excel-style) для этой колонки.</summary>
    public bool AllowValueFilter { get; init; }

    /// <summary>Пользовательская подпись <c>true</c> для булевой колонки. <c>null</c> → «Да».</summary>
    public string? BoolTrueLabel { get; init; }

    /// <summary>Пользовательская подпись <c>false</c> для булевой колонки. <c>null</c> → «Нет».</summary>
    public string? BoolFalseLabel { get; init; }

    /// <summary>Дескриптор типа — единый источник операторов, парсинга и формата.</summary>
    public ColumnTypes.ColumnTypeDescriptor Type { get; init; } = null!;
}

/// <summary>
/// Интерфейс регистрации метаданных колонок и настроек источника данных,
/// реализуемый <see cref="ClayGrid{TEntity}"/>.
/// Используется:
/// <list type="bullet">
///   <item><see cref="ClayColumnDef"/> — регистрация метаданных через каскадный параметр</item>
///   <item><see cref="ClayColumn{TEntity}"/> — поиск метаданных по числовому <c>ColumnId</c> для построения заголовка</item>
///   <item><see cref="ClayGridPageBase{T}"/> — чтение SQL-настроек грида</item>
/// </list>
/// </summary>
public interface IClayGrid
{
    /// <summary>
    /// Срабатывает при регистрации или отмене регистрации любой колонки.
    /// <see cref="ClayColumn{TEntity}"/> подписывается на это событие и вызывает
    /// <c>StateHasChanged</c>, чтобы отобразить <c>DisplayName</c> после того,
    /// как <see cref="ClayColumnDef"/> зарегистрирует метаданные.
    /// </summary>
    event Action? ColumnsChanged;

    /// <summary>
    /// Срабатывает при открытии или закрытии панели группировки или фильтрации.
    /// <see cref="ClayColumn{TEntity}"/> подписывается и вызывает <c>StateHasChanged</c>,
    /// чтобы показать или скрыть кнопку меню (⋮) в заголовке колонки.
    /// </summary>
    event Action? TrayStateChanged;

    /// <summary>
    /// Действующие настройки грида. Единая точка чтения конфигурации для страницы
    /// и дочерних компонентов вместо поштучных членов интерфейса.
    /// </summary>
    ClayGridOptions Options { get; }

    /// <summary>
    /// Возвращает <c>true</c> если колонка в данный момент участвует в группировке.
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки.</param>
    bool IsGrouped(string sqlName);

    /// <summary>
    /// Переключает серверную сортировку по колонке: нет → ASC → DESC → нет.
    /// Принимает <c>sqlName</c> колонки; реальное имя для ORDER BY резолвится
    /// из <see cref="ClayColumnMeta.SortName"/>.
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки (идентификатор).</param>
    Task ToggleSort(string sqlName);

    /// <summary>
    /// Возвращает фрагмент разметки с бейджем сортировки для колонки
    /// (номер приоритета + стрелка направления), либо пустой фрагмент если колонка не сортируется.
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки.</param>
    RenderFragment GetSortBadge(string sqlName);

    /// <summary>
    /// Возвращает метаданные колонки по её <c>SqlName</c>.
    /// Используется для drag-and-drop, группировки и фильтрации.
    /// Возвращает <c>null</c>, если колонка не зарегистрирована.
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки.</param>
    ClayColumnMeta? GetColumnMeta(string sqlName);

    /// <summary>
    /// Возвращает метаданные колонки по её числовому <c>ColumnId</c>.
    /// Используется компонентом <see cref="ClayColumn{TEntity}"/> для получения
    /// <c>DisplayName</c>, <c>SqlName</c> и <c>SortName</c> при построении заголовка.
    /// Возвращает <c>null</c>, если колонка не зарегистрирована.
    /// </summary>
    /// <param name="columnId">Числовой идентификатор колонки.</param>
    ClayColumnMeta? GetColumnMetaById(int columnId);

    /// <summary>
    /// Регистрирует колонку в гриде.
    /// Вызывается из <see cref="ClayColumnDef.OnInitialized"/>.
    /// </summary>
    /// <param name="columnId">Числовой идентификатор — связь с <see cref="ClayColumn{TEntity}"/>.</param>
    /// <param name="sqlName">SQL-имя (выходное имя SELECT) — идентификатор для SQL-операций.</param>
    /// <param name="displayName">Отображаемое имя (для заголовков, чипов в треях).</param>
    /// <param name="groupable">Разрешить группировку по этой колонке.</param>
    /// <param name="filterable">Разрешить фильтрацию по этой колонке.</param>
    /// <param name="sortName">
    /// Имя для ORDER BY. Если <c>null</c> — используется <paramref name="sqlName"/>.
    /// </param>
    /// <param name="allowValueFilter">
    /// Разрешить фильтр по уникальным значениям (Excel-style). По умолчанию <c>false</c>.
    /// </param>
    /// <param name="boolTrueLabel">
    /// Пользовательская подпись <c>true</c> для булевых колонок. <c>null</c> → «Да».
    /// </param>
    /// <param name="boolFalseLabel">
    /// Пользовательская подпись <c>false</c> для булевых колонок. <c>null</c> → «Нет».
    /// </param>
    void RegisterColumn(int columnId, string sqlName, string displayName, bool groupable, bool filterable, string? sortName = null, bool allowValueFilter = false, string? boolTrueLabel = null, string? boolFalseLabel = null);

    /// <summary>
    /// Отменяет регистрацию колонки.
    /// Вызывается из <see cref="ClayColumnDef.Dispose"/>.
    /// </summary>
    /// <param name="columnId">Числовой идентификатор колонки.</param>
    /// <param name="sqlName">SQL-имя колонки.</param>
    void UnregisterColumn(int columnId, string sqlName);

    /// <summary>
    /// Открыта ли панель группировки в данный момент.
    /// </summary>
    bool IsGroupingTrayExpanded { get; }

    /// <summary>
    /// Открыта ли панель фильтрации в данный момент.
    /// </summary>
    bool IsFilterTrayExpanded { get; }

    /// <summary>
    /// Добавляет колонку в трей группировки (альтернатива drag-and-drop для мобильных).
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки.</param>
    Task AddGroupAsync(string sqlName);

    /// <summary>
    /// Открывает диалог фильтрации для колонки (альтернатива drag-and-drop для мобильных).
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки.</param>
    Task AddFilterAsync(string sqlName);

    /// <summary>
    /// Текущий корень дерева составного фильтра.
    /// null или пустой узел — фильтрация не активна.
    /// </summary>
    ClayFilterGroupNode? ActiveCompositeFilter { get; }

    /// <summary>
    /// Восстанавливает дерево фильтра из внешнего источника (например, из URL)
    /// и немедленно перезагружает данные.
    /// </summary>
    void RestoreFilter(ClayFilterGroupNode root);

    /// <summary>
    /// Открывает диалог настраиваемого (составного) фильтра.
    /// UI-реализация — задача 11; здесь может быть заглушка.
    /// </summary>
    Task OpenCompositeFilterDialog();

    /// <summary>
    /// Доступен ли фильтр по значению (Excel-style) для колонки.
    /// Условия: глобальный <c>EnableValueFilter</c> + <c>Filterable</c> + <c>AllowValueFilter</c>.
    /// </summary>
    bool IsValueFilterAvailable(string sqlName);

    /// <summary>
    /// Активен ли фильтр по значению для колонки (есть <see cref="ValueFilter"/> с <c>HasValue</c>).
    /// Используется для подсветки значка в заголовке (треб. 13).
    /// </summary>
    bool IsValueFilterActive(string sqlName);

    /// <summary>
    /// Открывает диалог фильтра по уникальным значениям (<see cref="ClayColumnValueFilterDialog"/>)
    /// для колонки с ленивой загрузкой через <see cref="IClayGridDataLoader.LoadDistinctValuesAsync"/>.
    /// </summary>
    Task OpenValueFilterDialog(string sqlName);

    /// <summary>
    /// Регистрирует колонку в порядке отображения.
    /// Вызывается из <see cref="ClayColumn{TEntity}"/> при инициализации.
    /// </summary>
    /// <param name="columnId">Числовой идентификатор колонки.</param>
    void RegisterColumnInOrder(int columnId);

    /// <summary>
    /// Возвращает <c>true</c> если колонка скрыта пользователем через диалог настройки колонок.
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки.</param>
    bool IsColumnHidden(string sqlName);

    /// <summary>
    /// Возвращает метаданные видимых колонок в порядке отображения (без скрытых).
    /// </summary>
    IReadOnlyList<ClayColumnMeta> GetVisibleColumns();

    /// <summary>
    /// Регистрирует CellTemplate колонки для динамического рендеринга.
    /// Вызывается из <see cref="ClayColumn{TEntity}"/> при инициализации.
    /// </summary>
    /// <param name="columnId">Числовой идентификатор колонки.</param>
    /// <param name="template">Шаблон содержимого ячейки (приводится к RenderFragment при использовании).</param>
    void RegisterCellTemplate(int columnId, object template);
}
