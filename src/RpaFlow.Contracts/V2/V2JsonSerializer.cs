using System.Text.Json;
using System.Text.Json.Serialization;

namespace RpaFlow.Contracts.V2;

public static class V2JsonSerializer
{
    public static JsonSerializerOptions ReadOptions { get; } = CreateOptions(
        writeIndented: false);

    public static JsonSerializerOptions WriteOptions { get; } = CreateOptions(
        writeIndented: true);

    public static T Deserialize<T>(string json, string description)
        where T : class =>
        JsonSerializer.Deserialize<T>(json, ReadOptions)
        ?? throw new InvalidOperationException($"O documento {description} está vazio.");

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, WriteOptions);

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 64,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}
