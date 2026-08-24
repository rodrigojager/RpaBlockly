using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RpaFlow.Contracts.V2;

namespace RpaFlow.Packages;

public static class CanonicalJson
{
    public static byte[] Serialize<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, V2JsonSerializer.ReadOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false,
                SkipValidation = false
            });
        WriteElement(writer, element);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    public static string ComputePackageHash(RpaPackageDocuments documents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendDocument(hash, "flow.production.json", Serialize(documents.Flow));
        AppendDocument(hash, "locators.production.json", Serialize(documents.Locators));
        AppendDocument(hash, "rpa.policy.json", Serialize(documents.Policy));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendDocument(
        IncrementalHash hash,
        string name,
        ReadOnlySpan<byte> content)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name));
        hash.AppendData([0]);
        hash.AppendData(content);
        hash.AppendData([0]);
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
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
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException(
                    $"JSON canônico não aceita {element.ValueKind}.");
        }
    }
}
