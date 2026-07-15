using System.Collections;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Строка динамического грида. Одновременно:
/// — <see cref="IDetailRow"/> для сервисной колонки (карандаш/корзина) и экспорта;
/// — словарь колонка→значение для cell-шаблонов, собранных в <c>InitDynamicMode</c>.
/// </summary>
public sealed class ClayDynamicRow : IClayGridRow, IDetailRow, IReadOnlyDictionary<string, object?>
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    /// <summary>
    /// Оборачивает строку-словарь, полученную из <see cref="DynamicSql.QueryPagedRowsAsync"/>.
    /// </summary>
    public ClayDynamicRow(IReadOnlyDictionary<string, object?> values) => _values = values;

    /// <summary>Сама строка и есть сущность — cell-шаблоны разбирают её как словарь.</summary>
    object? IDetailRow.Item => this;

    /// <inheritdoc />
    public object? this[string key] => _values[key];

    /// <inheritdoc />
    public IEnumerable<string> Keys => _values.Keys;

    /// <inheritdoc />
    public IEnumerable<object?> Values => _values.Values;

    /// <inheritdoc />
    public int Count => _values.Count;

    /// <inheritdoc />
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
