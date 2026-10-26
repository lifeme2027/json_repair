using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonRepairNet.Internal;

internal static class JsonText
{
    public static string Write(JVal value, JsonRepairOptions options)
    {
        var indent = options.WriteIndented ? options.IndentSize : 0;
        var builder = new StringBuilder();
        WriteValue(builder, value, 0, indent);
        return builder.ToString();
    }

    public static string WriteNode(JsonNode? node, int indentSize)
    {
        var builder = new StringBuilder();
        WriteNode(builder, node, 0, indentSize);
        return builder.ToString();
    }

    public static JsonNode? ToNode(JVal value) => value switch
    {
        JNull => null,
        JBool boolean => JsonValue.Create(boolean.Value),
        JNum number => JsonNode.Parse(number.Literal, documentOptions: JsonValidity.DocumentOptions),
        JStr text => JsonValue.Create(text.Value),
        JArr array => ToArray(array),
        JObj obj => ToObject(obj),
        _ => throw new InvalidOperationException("Unknown JSON value."),
    };

    public static JVal FromNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return JNull.Instance;
            case JsonObject obj:
            {
                var result = new JObj();
                foreach (var pair in obj)
                {
                    result.Set(pair.Key, FromNode(pair.Value));
                }

                return result;
            }
            case JsonArray array:
            {
                var result = new JArr();
                foreach (var item in array)
                {
                    result.Items.Add(FromNode(item));
                }

                return result;
            }
            case JsonValue value when value.TryGetValue<bool>(out var boolean):
                return JBool.From(boolean);
            case JsonValue value when value.TryGetValue<string>(out var text):
                return new JStr(text);
            case JsonValue value:
            {
                var raw = value.ToJsonString();
                return raw == "null" ? JNull.Instance : new JNum(raw);
            }
            default:
                return JNull.Instance;
        }
    }

    private static JsonArray ToArray(JArr array)
    {
        var node = new JsonArray();
        foreach (var item in array.Items)
        {
            node.Add(ToNode(item));
        }

        return node;
    }

    private static JsonObject ToObject(JObj obj)
    {
        var node = new JsonObject();
        foreach (var prop in obj.Props)
        {
            node[prop.Key] = ToNode(prop.Value);
        }

        return node;
    }

    private static void WriteValue(StringBuilder builder, JVal value, int depth, int indent)
    {
        switch (value)
        {
            case JNull:
                builder.Append("null");
                break;
            case JBool boolean:
                builder.Append(boolean.Value ? "true" : "false");
                break;
            case JNum number:
                builder.Append(number.Literal);
                break;
            case JStr text:
                WriteString(builder, text.Value);
                break;
            case JArr array:
                WriteArray(builder, array, depth, indent);
                break;
            case JObj obj:
                WriteObject(builder, obj, depth, indent);
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteNode(StringBuilder builder, JsonNode? node, int depth, int indent)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                WriteJsonObject(builder, obj, depth, indent);
                break;
            case JsonArray array:
                WriteJsonArray(builder, array, depth, indent);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                WriteString(builder, text);
                break;
            case JsonValue value:
                builder.Append(value.ToJsonString());
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JObj obj, int depth, int indent)
    {
        if (obj.Props.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        if (indent == 0)
        {
            builder.Append('{');
            for (var i = 0; i < obj.Props.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteString(builder, obj.Props[i].Key);
                builder.Append(':');
                WriteValue(builder, obj.Props[i].Value, depth + 1, 0);
            }

            builder.Append('}');
            return;
        }

        builder.Append("{\n");
        var pad = new string(' ', (depth + 1) * indent);
        for (var i = 0; i < obj.Props.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",\n");
            }

            builder.Append(pad);
            WriteString(builder, obj.Props[i].Key);
            builder.Append(": ");
            WriteValue(builder, obj.Props[i].Value, depth + 1, indent);
        }

        builder.Append('\n');
        builder.Append(' ', depth * indent);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, JArr array, int depth, int indent)
    {
        if (array.Items.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        if (indent == 0)
        {
            builder.Append('[');
            for (var i = 0; i < array.Items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteValue(builder, array.Items[i], depth + 1, 0);
            }

            builder.Append(']');
            return;
        }

        builder.Append("[\n");
        var pad = new string(' ', (depth + 1) * indent);
        for (var i = 0; i < array.Items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",\n");
            }

            builder.Append(pad);
            WriteValue(builder, array.Items[i], depth + 1, indent);
        }

        builder.Append('\n');
        builder.Append(' ', depth * indent);
        builder.Append(']');
    }

    private static void WriteJsonObject(StringBuilder builder, JsonObject obj, int depth, int indent)
    {
        if (obj.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        if (indent == 0)
        {
            builder.Append('{');
            var first = true;
            foreach (var pair in obj)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteString(builder, pair.Key);
                builder.Append(':');
                WriteNode(builder, pair.Value, depth + 1, 0);
            }

            builder.Append('}');
            return;
        }

        builder.Append("{\n");
        var pad = new string(' ', (depth + 1) * indent);
        var index = 0;
        foreach (var pair in obj)
        {
            if (index > 0)
            {
                builder.Append(",\n");
            }

            builder.Append(pad);
            WriteString(builder, pair.Key);
            builder.Append(": ");
            WriteNode(builder, pair.Value, depth + 1, indent);
            index++;
        }

        builder.Append('\n');
        builder.Append(' ', depth * indent);
        builder.Append('}');
    }

    private static void WriteJsonArray(StringBuilder builder, JsonArray array, int depth, int indent)
    {
        if (array.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        if (indent == 0)
        {
            builder.Append('[');
            for (var i = 0; i < array.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteNode(builder, array[i], depth + 1, 0);
            }

            builder.Append(']');
            return;
        }

        builder.Append("[\n");
        var pad = new string(' ', (depth + 1) * indent);
        for (var i = 0; i < array.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",\n");
            }

            builder.Append(pad);
            WriteNode(builder, array[i], depth + 1, indent);
        }

        builder.Append('\n');
        builder.Append(' ', depth * indent);
        builder.Append(']');
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
