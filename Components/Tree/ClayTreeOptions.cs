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

    // ── Загрузка ─────────────────────────────────────────────────────────────────

    /// <summary>Ленивая загрузка уровней. В CT1 поддерживается только <c>true</c>.</summary>
    public bool LazyLoad { get; set; } = true;

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
}
