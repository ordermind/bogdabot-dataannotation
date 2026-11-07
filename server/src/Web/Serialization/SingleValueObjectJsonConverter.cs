using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Web.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SingleValueObjectJsonConverter<TValueObject, TPrimitive>
    : JsonConverter<TValueObject>
    where TValueObject : ISingleValueObject<TPrimitive, TValueObject>
{
    public override TValueObject Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<TPrimitive>(
            ref reader,
            options);

        return TValueObject.FromValue(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        TValueObject value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(
            writer,
            value.Value,
            options);
    }
}