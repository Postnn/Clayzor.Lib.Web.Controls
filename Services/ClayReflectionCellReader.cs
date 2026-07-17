using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Clayzor.Lib.Web.Controls.Components.Grid;

namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>
/// Чтение ячейки через рефлексию по типу сущности: SqlName → свойство с [Column("SqlName")]
/// (или совпадающим именем). Реализация для статического режима — поведение до GE1.
/// </summary>
public sealed class ClayReflectionCellReader : IClayGridCellReader
{
    private readonly Dictionary<string, PropertyInfo> _propMap;

    public ClayReflectionCellReader(Type entityType)
    {
        _propMap = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            _propMap[colAttr?.Name ?? prop.Name] = prop;
        }
    }

    /// <inheritdoc/>
    public bool TryGetCellValue(IDetailRow row, ClayColumnMeta column, out object? value, out Type valueType)
    {
        value     = null;
        valueType = typeof(string);

        var entity = row.Item;
        if (entity is null) return false;
        if (!_propMap.TryGetValue(column.SqlName, out var prop)) return false;

        value     = prop.GetValue(entity);
        valueType = prop.PropertyType;
        return true;
    }
}
