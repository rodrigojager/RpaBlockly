using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RpaFlow.Contracts;

public sealed class JsonFlowLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<FlowDefinition> LoadAsync(
        string path,
        CancellationToken cancellationToken) =>
        (await LoadSnapshotAsync(path, cancellationToken)).Definition;

    public async Task<FlowDefinitionSnapshot> LoadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Arquivo de fluxo de produção não encontrado.",
                fullPath);
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var json = StrictUtf8.GetString(bytes);
        FlowDefinition definition;
        try
        {
            definition = FlowJsonSerializer.Deserialize(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"O fluxo JSON não corresponde ao schema 1: {exception.Message}",
                exception);
        }

        FlowDefinitionValidator.Validate(definition);
        return new FlowDefinitionSnapshot(definition, fullPath, bytes);
    }
}

public sealed record FlowDefinitionSnapshot
{
    private readonly byte[] _utf8Bytes;

    public FlowDefinitionSnapshot(
        FlowDefinition definition,
        string fullPath,
        ReadOnlySpan<byte> utf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        Definition = definition;
        FullPath = fullPath;
        _utf8Bytes = utf8Bytes.ToArray();
        Sha256 = Convert.ToHexString(SHA256.HashData(_utf8Bytes));
    }

    public FlowDefinition Definition { get; }

    public string FullPath { get; }

    public byte[] Utf8Bytes => (byte[])_utf8Bytes.Clone();

    public int ByteLength => _utf8Bytes.Length;

    public string Sha256 { get; }
}
