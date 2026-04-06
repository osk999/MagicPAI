using System.Reflection;
using System.Text.Json;

namespace MagicPAI.Core.Services;

/// <summary>
/// Generates JSON Schema from C# types for structured output.
/// Works with both Claude (--json-schema inline) and Codex (--output-schema file).
///
/// Usage:
///   var schema = SchemaGenerator.FromType&lt;TriageResult&gt;();
///   var request = new AgentRequest { Prompt = "...", OutputSchema = schema };
/// </summary>
public static class SchemaGenerator
{
    /// <summary>
    /// Generate a JSON Schema string from a C# type.
    /// All public properties with { get; set; } or { get; init; } are included.
    /// Codex requires additionalProperties: false, so it's always set.
    /// </summary>
    public static string FromType<T>() => FromType(typeof(T));

    /// <summary>Generate a JSON Schema string from a System.Type.</summary>
    public static string FromType(Type type)
    {
        var schema = BuildObjectSchema(type);
        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = false });
    }

    private static Dictionary<string, object> BuildObjectSchema(Type type)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;

            var name = ToSnakeCase(prop.Name);
            properties[name] = MapType(prop.PropertyType);

            // Non-nullable types are required
            if (!IsNullable(prop.PropertyType))
                required.Add(name);
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false // Required by Codex
        };

        return schema;
    }

    private static Dictionary<string, object> MapType(Type type)
    {
        // Unwrap Nullable<T>
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            type = underlying;

        if (type == typeof(string))
            return new() { ["type"] = "string" };

        if (type == typeof(bool))
            return new() { ["type"] = "boolean" };

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return new() { ["type"] = "integer" };

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new() { ["type"] = "number" };

        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            return new()
            {
                ["type"] = "array",
                ["items"] = MapType(elementType)
            };
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            return new()
            {
                ["type"] = "array",
                ["items"] = MapType(elementType)
            };
        }

        if (type.IsEnum)
        {
            return new()
            {
                ["type"] = "string",
                ["enum"] = Enum.GetNames(type).Select(n => n.ToLowerInvariant()).ToArray()
            };
        }

        // Nested object
        if (type.IsClass && type != typeof(object))
            return BuildObjectSchema(type);

        return new() { ["type"] = "string" };
    }

    private static bool IsNullable(Type type)
    {
        if (!type.IsValueType) return true; // Reference types are nullable
        return Nullable.GetUnderlyingType(type) is not null;
    }

    private static string ToSnakeCase(string name)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
