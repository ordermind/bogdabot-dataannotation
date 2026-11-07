using Bogdabot.Server.Shared.ValueObjects;

namespace Bogdabot.Server.Web.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SingleValueObjectJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert
            .GetInterfaces()
            .Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ISingleValueObject<,>) &&
                i.GetGenericArguments()[1] == typeToConvert);
    }

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var interfaceType = typeToConvert
            .GetInterfaces()
            .Single(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ISingleValueObject<,>) &&
                i.GetGenericArguments()[1] == typeToConvert);

        var genericArguments = interfaceType.GetGenericArguments();

        var primitiveType = genericArguments[0];

        var converterType = typeof(SingleValueObjectJsonConverter<,>)
            .MakeGenericType(typeToConvert, primitiveType);

        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}