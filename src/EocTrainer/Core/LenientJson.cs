using System.Text.Json;
using System.Text.Json.Serialization;

namespace EocTrainer.Core;

/// <summary>
/// The state document is produced by the game's own JSON writer. An empty Lua
/// table is serialised as an empty JSON object in some extender builds, so lists
/// must accept <c>{}</c> as well as <c>[]</c>.
/// </summary>
public sealed class LenientListConverter<T> : JsonConverter<List<T>>
{
    public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return new List<T>();

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var depth = 1;
            while (depth > 0 && reader.Read())
            {
                depth += reader.TokenType switch
                {
                    JsonTokenType.StartObject => 1,
                    JsonTokenType.EndObject => -1,
                    _ => 0,
                };
            }
            return new List<T>();
        }

        var list = new List<T>();

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var item = JsonSerializer.Deserialize<T>(ref reader, options);
                if (item != null) list.Add(item);
            }
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}

/// <summary>Numbers may arrive as strings if a Lua number is not integral.</summary>
public sealed class LenientNumberConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => double.TryParse(reader.GetString(), out var parsed) ? parsed : null,
            JsonTokenType.True => 1,
            JsonTokenType.False => 0,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

/// <summary>
/// Lua values are not typed, so a field declared as string may legitimately hold
/// a number, a boolean or even a table. Everything is coerced instead of throwing.
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDouble().ToString("0.###"),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.StartObject or JsonTokenType.StartArray =>
                JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
