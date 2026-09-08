using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Resolve o caminho do modelo ONNX do OCR embutido. Ordem: caminho absoluto
/// configurado, caminho relativo à pasta de configuração, variável
/// RPABLOCKLY_CAPTCHA_MODEL e convenção ./captcha-models/common.onnx.
/// </summary>
internal static class OcrModelResolver
{
    private static readonly ConcurrentDictionary<string, string> ValidatedHashes =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Resolve(string? configuredPath, string configurationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (Path.IsPathFullyQualified(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            return Path.GetFullPath(Path.Combine(configurationDirectory, configuredPath));
        }

        var environmentPath = Environment.GetEnvironmentVariable("RPABLOCKLY_CAPTCHA_MODEL");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return Path.GetFullPath(environmentPath);
        }

        return Path.GetFullPath(
            Path.Combine(configurationDirectory, "captcha-models", "common.onnx"));
    }

    public static OcrModelDescriptor Validate(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        var fullPath = Path.GetFullPath(modelPath);
        if (!File.Exists(fullPath))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMissing,
                $"O modelo OCR não existe em '{fullPath}'. " +
                "Execute tools/Get-CaptchaModels.ps1 ou ajuste Captcha.OcrModelPath.");
        }

        var descriptor = OcrModelManifest.Get();
        var info = new FileInfo(fullPath);
        if (info.Length != descriptor.ModelSizeBytes)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMismatch,
                $"O modelo OCR '{fullPath}' tem {info.Length} bytes; " +
                $"o manifesto exige {descriptor.ModelSizeBytes} bytes.");
        }

        var signature = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        if (!ValidatedHashes.TryGetValue(fullPath, out var cachedSignature) ||
            !cachedSignature.Equals(signature, StringComparison.Ordinal))
        {
            using var stream = File.OpenRead(fullPath);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!hash.Equals(descriptor.ModelSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ModelMismatch,
                    $"O SHA-256 do modelo OCR '{fullPath}' é {hash}; " +
                    $"o manifesto exige {descriptor.ModelSha256}.");
            }

            ValidatedHashes[fullPath] = signature;
        }

        return descriptor;
    }
}
