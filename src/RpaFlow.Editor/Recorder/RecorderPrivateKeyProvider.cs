using System.Security.Cryptography;

namespace RpaFlow.Editor.Recorder;

/// <summary>
/// Fronteira exclusivamente backend para uma futura política autorizada de
/// descriptografia. A implementação padrão não possui chave privada e o wizard
/// remapeia referências sem devolver segredo ao JavaScript.
/// </summary>
internal interface IRecorderPrivateKeyProvider
{
    ValueTask<RSA?> GetPrivateKeyAsync(
        string recipientKeyId,
        CancellationToken cancellationToken);
}

internal sealed class DisabledRecorderPrivateKeyProvider : IRecorderPrivateKeyProvider
{
    public ValueTask<RSA?> GetPrivateKeyAsync(
        string recipientKeyId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<RSA?>(null);
    }
}
