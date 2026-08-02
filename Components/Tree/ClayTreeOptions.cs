using Clayzor.Lib.Entities.Tree;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

/// <summary>
/// Настройки одного экземпляра дерева <see cref="ClayTreeView"/> на странице.
/// <para>
/// Объект создаётся страницей ОДИН РАЗ и хранится в поле, а не собирается выражением
/// в разметке: грид сравнивает ссылку на параметр, и новый объект на каждый рендер
/// приводит к лишним пересчётам.
/// </para>
/// <para>
/// Не путать с <c>ClayTreeSettings</c> (появится в CT5): тот — настройки уровня приложения
/// (имена таблиц, префиксы параметров), байндятся из appsettings и живут в DI; этот —
/// настройки конкретного дерева на конкретной странице.
/// </para>
/// </summary>
public sealed class ClayTreeOptions
{
    // ── Идентификация ────────────────────────────────────────────────────────────

    /// <summary>
    /// Уникальный идентификатор дерева на странице. Обязателен к заполнению —
    /// используется как ключ сохранения состояния.
    /// </summary>
    public string TreeId { get; set; } = "";

    // ── Источник данных ──────────────────────────────────────────────────────────

    /// <summary>Базовый SQL-запрос SELECT (без WHERE / ORDER BY). Обязателен к заполнению.</summary>
    public string SelectSql { get; set; } = "";

    /// <summary>Модель хранения иерархии: вложенные множества или ссылка на родителя.</summary>
    public ClayTreeHierarchyMode HierarchyMode { get; set; } = ClayTreeHierarchyMode.NestedSet;

    /// <summary>Схема колонок источника данных (имена колонок, доп. поля).</summary>
    public ClayTreeSchema Schema { get; set; } = new();

    /// <summary>
    /// Пользовательский ORDER BY. Если не задан, используется сортировка по умолчанию:
    /// <c>NestedSet</c> — по левому ключу, <c>ParentKey</c> — по текстовой колонке.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>Идентификатор корневого узла. Если не задан — загружаются все корни.</summary>
    public object? RootId { get; set; }

    /// <summary>
    /// Имя строки подключения в web.config для этого дерева.
    /// Если не задана — используется основная строка подключения приложения
    /// (инжектированный <see cref="DbManager"/>).
    /// </summary>
    public string? ConnectionStringName { get; set; }

    // ── Загрузка ─────────────────────────────────────────────────────────────────

    /// <summary>Ленивая загрузка уровней. В CT1 поддерживается только <c>true</c>.</summary>
    public bool LazyLoad { get; set; } = true;

    /// <summary>
    /// Размер порции при постраничной загрузке уровня. 0 — пагинация выключена (уровень целиком).
    /// Действует ТОЛЬКО в режиме NestedSet; в ParentKey игнорируется (нет ключа L для кейсета).
    /// </summary>
    public int LevelPageSize { get; set; } = 0;

    /// <summary>Способ запроса следующей порции уровня: кнопкой или автоподгрузкой при скролле.</summary>
    public ClayTreeLevelPagingMode LevelPagingMode { get; set; } = ClayTreeLevelPagingMode.Button;

    /// <summary>
    /// Начальный уровень раскрытия (0 — только корни). Применяется только если
    /// нет сохранённого состояния.
    /// </summary>
    public int InitialExpandLevel { get; set; }

    // ── Состояние ────────────────────────────────────────────────────────────────

    /// <summary>Сохранять и восстанавливать раскрытое состояние дерева.</summary>
    public bool PersistExpandedState { get; set; } = true;

    // ── Внешний вид ──────────────────────────────────────────────────────────────

    /// <summary>Отступ на уровень в пикселях.</summary>
    public int IndentPx { get; set; } = 20;

    /// <summary>Показывать индикатор загрузки при подгрузке уровня.</summary>
    public bool ShowLoadingIndicator { get; set; } = true;

    /// <summary>Показывать глобальный оверлей .clay-busy при загрузке данных.</summary>
    public bool ShowBusyOverlay { get; set; } = true;

    /// <summary>Показывать направляющие линии иерархии (вертикали по уровням + ус к узлу).</summary>
    public bool ShowLines { get; set; } = false;

    /// <summary>
    /// Дополнительный CSS-класс корневого контейнера. Конфигурация, а не данные —
    /// поэтому в options, а не атрибутом тега.
    /// </summary>
    public string? Class { get; set; }

    /// <summary>
    /// Дополнительные inline-стили корневого контейнера. Конфигурация, а не данные —
    /// поэтому в options, а не атрибутом тега.
    /// </summary>
    public string? Style { get; set; }

    // ── Фильтрация ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Максимум совпадений, показываемых при фильтре (предки не в счёт).
    /// 0 — без лимита.
    /// </summary>
    public int MaxFilterRecords { get; set; } = 100;

    /// <summary>SQL-имена колонок, исключённых из фильтрации (не предлагаются в диалоге).</summary>
    public IReadOnlyList<string> FilterExcludedColumns { get; set; } = [];

    /// <summary>
    /// Значения фильтра по умолчанию: SqlName → значение.
    /// Подставляются как WHERE в ленивом режиме загрузки уровней.
    /// </summary>
    public IReadOnlyDictionary<string, object?> FilterDefaults { get; set; } = new Dictionary<string, object?>();

    /// <summary>
    /// Явный список колонок дерева, доступных для фильтрации.
    /// Если не задан или после исключений пуст — кнопка фильтра не показывается.
    /// </summary>
    public IReadOnlyList<ClayTreeFilterColumn>? FilterColumns { get; set; }

    // ── Выбор ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Режим выделения узлов.
    /// <see cref="ClayTreeSelectionMode.Single"/> — одиночный клик подсвечивает узел.
    /// <see cref="ClayTreeSelectionMode.Multiple"/> — задел, в текущей версии не реализовано.
    /// </summary>
    public ClayTreeSelectionMode SelectionMode { get; set; } = ClayTreeSelectionMode.Single;
}
