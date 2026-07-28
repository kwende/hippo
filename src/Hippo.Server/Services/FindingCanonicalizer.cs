using System.Security.Cryptography;
using System.Text.Json;
using Hippo.Server.Contracts;

namespace Hippo.Server.Services;

public sealed class FindingCanonicalizer
{
    public byte[] ComputeHash(ProposedFinding finding)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();

        writer.WriteString(
            "kind",
            Normalize(finding.Kind));

        writer.WriteString(
            "predicate",
            Normalize(finding.Predicate));

        writer.WritePropertyName("value");
        if (finding.Value is { } value)
        {
            WriteCanonicalElement(writer, value);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WritePropertyName("entities");
        writer.WriteStartArray();

        foreach (var entity in finding.Entities
                     .Select(entity => new
                     {
                         Kind = Normalize(entity.Kind),
                         Key = Normalize(entity.CanonicalKey),
                         Role = Normalize(entity.Role)
                     })
                     .Distinct()
                     .OrderBy(entity => entity.Kind, StringComparer.Ordinal)
                     .ThenBy(entity => entity.Key, StringComparer.Ordinal)
                     .ThenBy(entity => entity.Role, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", entity.Kind);
            writer.WriteString("key", entity.Key);
            writer.WriteString("role", entity.Role);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteString(
            "effectiveFromMinute",
            finding.EffectiveFrom?.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm'Z'"));

        writer.WriteString(
            "effectiveToMinute",
            finding.EffectiveTo?.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm'Z'"));

        writer.WritePropertyName("evidence");
        writer.WriteStartArray();

        foreach (var evidence in finding.Evidence
                     .Select(evidence => new
                     {
                         Type = Normalize(evidence.Type),
                         Reference = NormalizeNullable(evidence.SourceReference),
                         Hash = NormalizeNullable(evidence.Sha256)
                     })
                     .Distinct()
                     .OrderBy(evidence => evidence.Type, StringComparer.Ordinal)
                     .ThenBy(
                         evidence => evidence.Reference,
                         StringComparer.Ordinal)
                     .ThenBy(evidence => evidence.Hash, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("type", evidence.Type);
            writer.WriteString("reference", evidence.Reference);
            writer.WriteString("hash", evidence.Hash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return SHA256.HashData(stream.ToArray());
    }

    private static void WriteCanonicalElement(
        Utf8JsonWriter writer,
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element
                             .EnumerateObject()
                             .OrderBy(
                                 property => property.Name,
                                 StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                element.WriteTo(writer);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported JSON value kind {element.ValueKind}.");
        }
    }

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            value.Trim()
                .ToLowerInvariant()
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Normalize(value);
}
