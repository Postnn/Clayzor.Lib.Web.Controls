using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Настройки одного экземпляра грида <see cref="ClayGrid{TEntity}"/> на странице:
/// источник данных, состав тулбара, поведение колонок, фильтрации и экспорта.
/// <para>
/// Объект создаётся страницей ОДИН РАЗ и хранится в поле, а не собирается выражением
/// в разметке: грид сравнивает ссылку на параметр, и новый объект на каждый рендер
/// приводит к лишним пересчётам.
/// </para>
/// <para>
/// Не путать с <see cref="ClayGridDynamicOptions"/>: тот — настройки уровня
/// приложения (имена таблиц справочника гридов, префиксы пользовательских параметров),
/// байндятся из appsettings и живут в DI; этот — настройки конкретного грида на
/// конкретной странице.
/// </para>
/// </summary>
public sealed class ClayGridOptions
{
    /// <summary>
    /// Значения по умолчанию. Используется <see cref="ClayGridPageBase{T}"/> как фолбэк
    /// при отсутствии грида (<c>Grid?.Options ?? ClayGridOptions.Defaults</c>).
    /// Экземпляр общий и не должен изменяться — только чтение.
    /// </summary>
    public static ClayGridOptions Defaults { get; } = new();

    // ── Источник данных ──────────────────────────────────────────────────────────

    /// <summary>Базовый SQL-запрос SELECT (без WHERE / ORDER BY).</summary>
    public string SelectSql { get; set; } = string.Empty;

    /// <summary>Выходные имена колонок SELECT для полнотекстового поиска.</summary>
    public string[] SearchColumns { get; set; } = [];

    /// <summary>Порядок сортировки по умолчанию.</summary>
    public string DefaultOrder { get; set; } = string.Empty;

    /// <summary>Количество строк на странице по умолчанию.</summary>
    public int PageSize { get; set; } = 50;

    // ── Внешний вид ──────────────────────────────────────────────────────────────

    /// <summary>Заголовок грида.</summary>
    public string Title { get; set; } = "Список";

    /// <summary>DOM-идентификатор корневого элемента грида.</summary>
    public string Id { get; set; } = "clay-grid";

    /// <summary>Показывать кнопку «Добавить» в тулбаре.</summary>
    public bool ShowAddButton { get; set; } = true;

    /// <summary>Показывать панель пагинации.</summary>
    public bool ShowPagination { get; set; } = true;

    // ── Колонки ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Режим отображения кнопки меню (⋮) в заголовках колонок.
    /// <c>Hidden</c> — всегда скрыта, <c>Always</c> — всегда видна,
    /// <c>Mobile</c> — только на мобильных (≤960px, по умолчанию).
    /// </summary>
    public ColumnMenuMode ColumnMenuMode { get; set; } = ColumnMenuMode.Mobile;

    /// <summary>Разрешить перетаскивание колонок.</summary>
    public bool AllowColumnReorder { get; set; } = true;

    // ── Фильтрация ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Глобальное включение фильтра по значению (Excel-style) для всех колонок.
    /// При <c>false</c> значки фильтра по значению не отображаются, даже если
    /// на отдельных колонках установлен <c>AllowValueFilter</c>=<c>true</c>.
    /// По умолчанию <c>true</c>. Используется начиная с задачи V7.
    /// </summary>
    public bool EnableValueFilter { get; set; } = true;

    /// <summary>
    /// Тип данных для каждой фильтруемой колонки: ключ — SQL-имя, значение — <see cref="ColumnType"/>.
    /// Передаётся из <see cref="ClayGridPageBase{T}.FilterColumnTypes"/>.
    /// </summary>
    public IReadOnlyDictionary<string, ColumnType> FilterColumnTypes { get; set; }
        = new Dictionary<string, ColumnType>();

    /// <summary>
    /// Необязательный источник вариантов для выпадающего списка значений в диалоге фильтра.
    /// Ключ — SQL-имя колонки, значение — список вариантов (<see cref="ClayFilterOption"/>).
    /// Передаётся со страницы, которая может переопределить <see cref="ClayGridPageBase{T}.FilterLookupOptions"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>>? FilterLookupOptions { get; set; }

    // ── Редактирование ───────────────────────────────────────────────────────────

    /// <summary>
    /// Тип компонента диалога редактирования.
    /// Диалог должен принимать параметр <c>Model</c> типа сущности.
    /// </summary>
    public Type? EditDialogType { get; set; }

    /// <summary>
    /// Текст уведомления после успешного сохранения записи через сервисную колонку.
    /// </summary>
    public string EditSuccessMessage { get; set; } = "Запись обновлена";

    // ── Выбор и групповые операции ───────────────────────────────────────────────

    /// <summary>Показывать кнопку выбора записей (чекбоксы).</summary>
    public bool SelectVisible { get; set; }

    /// <summary>Показывать группу «Печать» в меню групповых операций.</summary>
    public bool ShowPrint { get; set; }

    /// <summary>Показывать группу «Выгрузка в Excel» в меню групповых операций.</summary>
    public bool ShowExcel { get; set; }

    /// <summary>
    /// Кастомные группы операций для меню групповых операций.
    /// Каждая группа рендерится как подменю со своими операциями.
    /// Обработчики (<see cref="BatchOperation.OnExecute"/>) реализуются в приложении.
    /// </summary>
    public IReadOnlyList<BatchOperationGroup>? CustomBatchGroups { get; set; }

    // ── Динамический режим ───────────────────────────────────────────────────────

    /// <summary>Включает динамический режим (чтение определения из БД).</summary>
    public bool Dynamic { get; set; }

    /// <summary>
    /// Код запроса (GridId). Если не задан — берётся из query-параметра
    /// с именем <see cref="ClayGridDynamicOptions.GridIdQueryParam"/>.
    /// </summary>
    public int? DynamicGridId { get; set; }
}
