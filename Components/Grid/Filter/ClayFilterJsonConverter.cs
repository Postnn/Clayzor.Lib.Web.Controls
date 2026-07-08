using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// JSON-конвертер для полиморфного дерева фильтра (<see cref="IClayFilterNode"/>).
/// Использует дискриминатор <c>$type</c>:
/// <c>"group"</c> → <see cref="ClayFilterGroupNode"/>,
/// <c>"column"</c> → <see cref="ColumnFilter"/>,
/// <c>"value"</c> → <see cref="ValueFilter"/>.
/// Свойства <c>ParamName</c>, <c>SecondParamName</c>, <c>ParamPrefix</c>
/// и computed-свойства исключены из сериализации через <c>[JsonIgnore]</c>.
/// </summary>
public sealed class ClayFilterJsonConverter : JsonConverter<IClayFilterNode>
{
    private const string TypeDiscriminator = "$type";

    public override IClayFilterNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject for IClayFilterNode");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty(TypeDiscriminator, out var typeProp))
            throw new JsonException($"Missing '{TypeDiscriminator}' property in filter node");

        var nodeType = typeProp.GetString();

        return nodeType switch
        {
            "group"  => DeserializeGroup(root),
            "column" => DeserializeColumnFilter(root),
            "value"  => DeserializeValueFilter(root),
            _ => throw new JsonException($"Unknown filter node type: {nodeType}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, IClayFilterNode value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case ClayFilterGroupNode group:
                WriteGroup(writer, group);
                break;
            case ColumnFilter leaf:
                WriteColumnFilter(writer, leaf);
                break;
            case ValueFilter vf:
                WriteValueFilter(writer, vf);
                break;
            default:
                throw new JsonException($"Unknown filter node type: {value.GetType().Name}");
        }

        writer.WriteEndObject();
    }

    // ── Serialize ──────────────────────────────────────────────────────────

    private static void WriteGroup(Utf8JsonWriter writer, ClayFilterGroupNode group)
    {
        writer.WriteString(TypeDiscriminator, "group");
        writer.WriteNumber("logic", (int)group.Logic);
        writer.WriteStartArray("nodes");
        foreach (var node in group.Nodes)
            JsonSerializer.Serialize(writer, node, typeof(IClayFilterNode));
        writer.WriteEndArray();
    }

    private static void WriteColumnFilter(Utf8JsonWriter writer, ColumnFilter leaf)
    {
        writer.WriteString(TypeDiscriminator, "column");
        writer.WriteString("column", leaf.Column);
        writer.WriteNumber("operator", (int)leaf.Operator);
        writer.WriteNumber("source", (int)leaf.Source);

        // Значение
        WriteValue(writer, "value", leaf.Value);

        // Второе условие
        if (leaf.HasSecondClause)
        {
            writer.WriteNumber("logicalOperator", (int)leaf.LogicalOperator);
            writer.WriteNumber("secondOperator", (int)leaf.SecondOperator);
            WriteValue(writer, "secondValue", leaf.SecondValue);
        }
    }

    private static void WriteValueFilter(Utf8JsonWriter writer, ValueFilter vf)
    {
        writer.WriteString(TypeDiscriminator, "value");
        writer.WriteString("column", vf.Column);
        writer.WriteBoolean("negate", vf.Negate);
        writer.WriteBoolean("blankChecked", vf.BlankChecked);
        writer.WriteStartArray("values");
        foreach (var v in vf.Values)
            WriteValue(writer, (string?)null, v);
        writer.WriteEndArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, string? propertyName, object? value)
    {
        if (propertyName is not null)
            writer.WritePropertyName(propertyName);

        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value, value.GetType());
        }
    }

    // ── Deserialize ────────────────────────────────────────────────────────

    private static ClayFilterGroupNode DeserializeGroup(JsonElement root)
    {
        var group = new ClayFilterGroupNode();

        if (root.TryGetProperty("logic", out var logicProp))
            group.Logic = (LogicalOperator)logicProp.GetInt32();

        if (root.TryGetProperty("nodes", out var nodesProp) && nodesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var nodeEl in nodesProp.EnumerateArray())
            {
                var node = JsonSerializer.Deserialize<IClayFilterNode>(nodeEl.GetRawText());
                if (node is not null)
                    group.Nodes.Add(node);
            }
        }

        return group;
    }

    private static ColumnFilter DeserializeColumnFilter(JsonElement root)
    {
        var leaf = new ColumnFilter();

        if (root.TryGetProperty("column", out var colProp))
            leaf.Column = colProp.GetString() ?? "";
        if (root.TryGetProperty("operator", out var opProp))
            leaf.Operator = (ColumnFilterOperator)opProp.GetInt32();
        if (root.TryGetProperty("source", out var srcProp))
            leaf.Source = (ClayFilterSource)srcProp.GetInt32();
        if (root.TryGetProperty("value", out var valProp))
            leaf.Value = ElementToClrValue(valProp);

        if (root.TryGetProperty("logicalOperator", out var logicProp))
            leaf.LogicalOperator = (LogicalOperator)logicProp.GetInt32();
        if (root.TryGetProperty("secondOperator", out var secOpProp))
            leaf.SecondOperator = (ColumnFilterOperator)secOpProp.GetInt32();
        if (root.TryGetProperty("secondValue", out var secValProp))
            leaf.SecondValue = ElementToClrValue(secValProp);

        return leaf;
    }

    private static ValueFilter DeserializeValueFilter(JsonElement root)
    {
        var vf = new ValueFilter();

        if (root.TryGetProperty("column", out var colProp))
            vf.Column = colProp.GetString() ?? "";
        if (root.TryGetProperty("negate", out var negProp))
            vf.Negate = negProp.GetBoolean();
        if (root.TryGetProperty("blankChecked", out var blankProp))
            vf.BlankChecked = blankProp.GetBoolean();
        if (root.TryGetProperty("values", out var valsProp) && valsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in valsProp.EnumerateArray())
                vf.Values.Add(ElementToClrValue(v));
        }

        return vf;
    }

    /// <summary>
    /// Преобразует <see cref="JsonElement"/> в ближайший CLR-тип:
    /// string, int, long, double, bool, или null.
    /// </summary>
    private static object? ElementToClrValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null   => null,
        JsonValueKind.String => el.GetString(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Number => el.TryGetInt32(out var i) ? i
                              : el.TryGetInt64(out var l) ? l
                              : (object)el.GetDouble(),
        _                    => el.GetRawText(),
    };
}
