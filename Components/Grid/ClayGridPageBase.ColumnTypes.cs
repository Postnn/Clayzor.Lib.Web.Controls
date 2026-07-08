using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Clayzor.Lib.Entities;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public abstract partial class ClayGridPageBase<T> where T : Entity
{
    // ── Типы колонок для фильтрации — вычисляются автоматически ─────────────────

    /// <summary>
    /// Типы данных фильтруемых колонок, автоматически определённые по <see cref="ColumnAttribute"/>
    /// и C#-типам свойств сущности <typeparamref name="T"/>.
    /// Маппинг: SQL-имя колонки → <see cref="ColumnType"/> (Text / Number / Boolean).
    /// <para>
    /// SQL-имя берётся из <c>[Column("...")]</c>-атрибута, либо из имени свойства если атрибут отсутствует
    /// (так работает, например, <c>TestTypeName</c> — алиас из JOIN без <c>[Column]</c>).
    /// </para>
    /// Может быть переопределено на странице для нестандартного маппинга.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, ColumnType> FilterColumnTypes => _inferredColumnTypes;

    // Кеш вычисляется один раз для каждого конкретного T при инициализации класса
    private static readonly IReadOnlyDictionary<string, ColumnType> _inferredColumnTypes
        = InferFilterColumnTypes();

    /// <summary>
    /// Кеш маппинга SQL-имя колонки → <see cref="PropertyInfo"/> свойства сущности <typeparamref name="T"/>.
    /// Используется при построении групповых заголовков для Excel-экспорта всех данных —
    /// читает значения групповых колонок из свойств сущности по их SQL-именам.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, PropertyInfo> _propertyMap
        = BuildPropertyMap();

    /// <summary>
    /// Имя колонки первичного ключа в БД для сущности <typeparamref name="T"/>.
    /// Берётся из <see cref="ColumnAttribute"/> на свойстве <c>Id</c>, либо <c>"Id"</c>.
    /// </summary>
    private static readonly string _idColumnName = GetIdColumnName();

    private static string GetIdColumnName()
    {
        var idProp = typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProp == null) return "Id";
        var colAttr = idProp.GetCustomAttribute<ColumnAttribute>();
        return colAttr?.Name ?? "Id";
    }

    /// <summary>
    /// Строит словарь SQL-имя колонки → <see cref="PropertyInfo"/> через рефлексию
    /// по <see cref="ColumnAttribute"/> и свойствам <typeparamref name="T"/>.
    /// </summary>
    private static Dictionary<string, PropertyInfo> BuildPropertyMap()
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            map[colAttr?.Name ?? prop.Name] = prop;
        }
        return map;
    }

    /// <summary>
    /// Определяет типы колонок для фильтрации через рефлексию по свойствам <typeparamref name="T"/>.
    /// </summary>
    private static Dictionary<string, ColumnType> InferFilterColumnTypes()
    {
        var result = new Dictionary<string, ColumnType>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            var sqlName = colAttr?.Name ?? prop.Name;
            result[sqlName] = MapClrTypeToColumnType(prop.PropertyType);
        }
        return result;
    }

    /// <summary>
    /// Приводит C#-тип свойства к <see cref="ColumnType"/> для диалога фильтрации.
    /// <c>Nullable&lt;T&gt;</c> обрабатывается через <see cref="Nullable.GetUnderlyingType"/>.
    /// </summary>
    private static ColumnType MapClrTypeToColumnType(Type clrType)
        => ColumnTypes.ColumnTypeRegistry.FromClr(clrType).Kind;

    /// <summary>
    /// Необязательный источник вариантов для выпадающего списка значений фильтра.
    /// Ключ — SQL-имя колонки, значение — список вариантов (<see cref="ClayFilterOption"/>).
    /// Если для колонки задан список, в диалоге фильтра вместо текстового/числового поля
    /// показывается выпадающий список. Не меняет <see cref="ColumnType"/> и SQL.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>> FilterLookupOptions { get; }
        = new Dictionary<string, IReadOnlyList<ClayFilterOption>>();
}
