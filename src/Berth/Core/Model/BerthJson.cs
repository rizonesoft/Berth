using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    Converters = [typeof(UtcSecondsConverter)])]
[JsonSerializable(typeof(Layout))]
internal sealed partial class BerthJson : JsonSerializerContext
{
}

/// <summary>
/// Writes a time as UTC to the whole second, ending <c>Z</c> (SPEC §14: <c>2026-11-15T07:14:03Z</c>), and reads
/// any ISO 8601 time with an offset back as UTC.
/// </summary>
internal sealed class UtcSecondsConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string text = reader.GetString() ?? throw new JsonException("A time is null.");
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset value))
        {
            throw new JsonException($"'{text}' is not a time.");
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
