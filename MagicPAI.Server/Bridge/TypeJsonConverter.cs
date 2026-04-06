using System.Text.Json;
using System.Text.Json.Serialization;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Handles serialization of System.Type and System.RuntimeType.
/// Required for Elsa activity descriptor serialization in FastEndpoints.
/// </summary>
public class TypeJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeof(Type).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        new TypeConverterInner();

    private class TypeConverterInner : JsonConverter<Type>
    {
        public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var typeName = reader.GetString();
            return typeName is not null ? Type.GetType(typeName) : null;
        }

        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.FullName ?? value.Name);
        }
    }
}
