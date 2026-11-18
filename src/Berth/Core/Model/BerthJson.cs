using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Berth.Core.Model;

/// <summary>
/// The JSON shapes of SPEC §14, generated at build time so single-file trimming cannot break them. A missing or
/// null required value is a parse failure, never a default: a saved icon without its <c>x</c> must not restore to 0.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true,
    Converters = [typeof(UtcSecondsConverter), typeof(NonNullElementsConverterFactory)])]
[JsonSerializable(typeof(Layout))]
[JsonSerializable(typeof(Settings))]
internal sealed partial class BerthJson : JsonSerializerContext
{
    /// <summary>
    /// The one way Berth reads its JSON: a document that is not valid UTF-8 or JSON, breaks the shape, holds a null element or is
    /// just <c>null</c> throws <see cref="JsonException"/>, so every caller sees one kind of failure.
    /// </summary>
    public static T Parse<T>(ReadOnlySpan<byte> utf8, JsonTypeInfo<T> info) where T : class
    {
        // The reader skips values it does not map without decoding them, so the whole document is checked first:
        // a damaged byte anywhere means a damaged file, which must never pass for a readable one.
        if (!System.Text.Unicode.Utf8.IsValid(utf8))
        {
            throw new JsonException("The document is not valid UTF-8.");
        }

        return JsonSerializer.Deserialize(utf8, info) ?? throw new JsonException("The document is null.");
    }
}

/// <summary>Refuses a null element in any immutable array of the model, which nullable annotations do not cover.</summary>
internal sealed class NonNullElementsConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
        && !typeToConvert.GetGenericArguments()[0].IsValueType;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(NonNullElementsConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class NonNullElementsConverter<T> : JsonConverter<ImmutableArray<T>> where T : class
{
    public override ImmutableArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected an array.");
        }

        JsonTypeInfo<T> element = options.GetTypeInfo<T>();
        ImmutableArray<T>.Builder items = ImmutableArray.CreateBuilder<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(JsonSerializer.Deserialize(ref reader, element) ?? throw new JsonException("An array holds a null element."));
        }

        return items.ToImmutable();
    }

    public override void Write(Utf8JsonWriter writer, ImmutableArray<T> value, JsonSerializerOptions options)
    {
        JsonTypeInfo<T> element = options.GetTypeInfo<T>();
        writer.WriteStartArray();
        foreach (T item in value)
        {
            JsonSerializer.Serialize(writer, item, element);
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// Writes a time as UTC to the whole second, ending <c>Z</c> (SPEC §14: <c>2026-11-15T07:14:03Z</c>), and reads
/// any ISO 8601 time with an offset back as UTC.
/// </summary>
internal sealed partial class UtcSecondsConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string text = reader.GetString() ?? throw new JsonException("A time is null.");

        // A full date and time with Z or an offset, nothing less: "07:14" must not read as today.
        if (!Iso8601().IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value))
        {
            throw new JsonException($"'{text}' is not a date and time with Z or an offset.");
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})\z")]
    private static partial Regex Iso8601();
}
