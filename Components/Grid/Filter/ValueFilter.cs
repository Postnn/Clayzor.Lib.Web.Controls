namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Листовой узел дерева составного фильтра: фильтрация по набору выбранных значений
/// колонки (Excel-style / автофильтр). Поддерживает прямое (<c>IN (...)</c>) и
/// инвертированное (<c>NOT IN (...)</c>) применение, а также служебный пункт
/// «(пустые)» для включения NULL и пустых строк.
/// </summary>
/// <remarks>
/// Реализует <see cref="IClayFilterNode"/> наравне с <see cref="ColumnFilter"/>
/// и <see cref="ClayFilterGroupNode"/>. В одной колонке одновременно активен либо
/// <see cref="ColumnFilter"/> (условие), либо <see cref="ValueFilter"/> (набор
/// значений) — они взаимоисключающие.
/// SQL-генерация (IN / NOT IN, параметры, NULL/пустышки) — в задаче V2
/// (<see cref="ClayCompositeSqlBuilder"/>).
/// </remarks>
public sealed class ValueFilter : IClayFilterNode
{
    /// <summary>SQL-имя колонки (например, "НазваниеАнализа").</summary>
    public string Column { get; set; } = "";

    /// <summary>
    /// Литералы, которые реально уйдут в SQL-параметры (<c>IN</c> / <c>NOT IN</c>).
    /// Это меньшая из сторон (выбранные или невыбранные — в зависимости от
    /// <see cref="Negate"/>). Не путать с полным списком уникальных значений колонки.
    /// </summary>
    public List<object?> Values { get; set; } = [];

    /// <summary>
    /// Режим построения SQL: <c>false</c> → <c>IN (...)</c> (прямой),
    /// <c>true</c> → <c>NOT IN (...)</c> (инвертированный).
    /// Заполняется на этапе применения диалога (V6/V7) по правилу инверсии:
    /// когда не выбрано мало значений — инвертируем для краткости.
    /// </summary>
    public bool Negate { get; set; }

    /// <summary>
    /// Отмечен ли пользователем служебный пункт «(пустые)» — включает в результат
    /// строки с <c>NULL</c> и пустыми строками в колонке <see cref="Column"/>.
    /// Семантику в SQL разворачивает <see cref="ClayCompositeSqlBuilder"/> (V2).
    /// </summary>
    public bool BlankChecked { get; set; }

    /// <summary>
    /// Префикс имён Dapper-параметров (например, <c>"vf_НазваниеАнализа"</c>).
    /// Фактические имена назначает сквозной счётчик билдера; поле — для
    /// отладки и читаемости, в SQL не подставляется.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ParamPrefix { get; set; } = "";

    /// <summary>
    /// Возвращает <c>true</c>, если узел имеет значимое содержимое:
    /// хотя бы одно выбранное значение или отмечен пункт «(пустые)».
    /// Пустой узел не порождает SQL-фрагмента.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasValue => Values.Count > 0 || BlankChecked;

    /// <summary>
    /// Глубокое копирование узла. Создаёт новый экземпляр с независимой копией
    /// списка <see cref="Values"/> — правка копии не трогает оригинал.
    /// </summary>
    public IClayFilterNode Clone() => new ValueFilter
    {
        Column       = Column,
        Values       = [..Values],
        Negate       = Negate,
        BlankChecked = BlankChecked,
        ParamPrefix  = ParamPrefix,
    };
}
