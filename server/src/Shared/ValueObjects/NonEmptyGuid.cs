using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bogdabot.Server.Shared.ValueObjects;

[JsonConverter(typeof(NonEmptyGuidJsonConverter))]
public readonly record struct NonEmptyGuid : ISingleValueObject<Guid, NonEmptyGuid>
{
    public Guid Value { get; }

    public NonEmptyGuid(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Guid cannot be empty", nameof(value));

        Value = value;
    }

    public static NonEmptyGuid FromValue(Guid value) => new(value);

    public static NonEmptyGuid NewGuid() => new(Guid.CreateVersion7());

    public static implicit operator Guid(NonEmptyGuid g) => g.Value;

    public static implicit operator NonEmptyGuid(Guid g) => new(g);

    public override string ToString() => Value.ToString();

    public static NonEmptyGuid Parse(string s) => new(Guid.Parse(s));

    public static bool TryParse(string s, out NonEmptyGuid result)
    {
        result = default;
        if (!Guid.TryParse(s, out var g))
            return false;
        if (g == Guid.Empty) return false;
        result = new NonEmptyGuid(g);
        return true;
    }
}

internal class NonEmptyGuidJsonConverter : JsonConverter<NonEmptyGuid>
{
    public override NonEmptyGuid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString()!;
            if (NonEmptyGuid.TryParse(s, out var val)) return val;
            throw new JsonException("Invalid NonEmptyGuid value");
        }

        if (reader.TokenType == JsonTokenType.Null)
            throw new JsonException("NonEmptyGuid cannot be null");

        // try reading as Guid
        var g = reader.GetGuid();
        return new NonEmptyGuid(g);
    }

    public override void Write(Utf8JsonWriter writer, NonEmptyGuid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
