using System.Text.Json;

namespace RpaFlow.Playwright.V2;

internal static class OcrModelManifest
{
    private const string ResourceName =
        "RpaFlow.Playwright.V2.Captcha.captcha-models.manifest.json";

    private static readonly OcrModelDescriptor Descriptor = Load();

    public static OcrModelDescriptor Get() => Descriptor;

    private static OcrModelDescriptor Load()
    {
        var assembly = typeof(OcrModelManifest).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName) ??
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMissing,
                $"O manifesto OCR embutido '{ResourceName}' não foi encontrado.");
        var manifest = JsonSerializer.Deserialize<OcrModelManifestDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMismatch,
                "O manifesto OCR embutido está vazio.");
        if (manifest.SchemaVersion != 1 || manifest.Models.Count != 1)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMismatch,
                "O manifesto OCR deve usar schemaVersion 1 e declarar exatamente um modelo.");
        }

        return manifest.Models[0];
    }
}

internal sealed record OcrModelManifestDocument(
    int SchemaVersion,
    List<OcrModelDescriptor> Models);

internal sealed record OcrModelDescriptor(
    string Id,
    string Version,
    string PublishedFile,
    string ModelSha256,
    long ModelSizeBytes,
    OcrCharsetDescriptor Charset,
    OcrTensorDescriptor Tensors,
    OcrPreprocessingDescriptor Preprocessing);

internal sealed record OcrCharsetDescriptor(
    string Path,
    string Sha256,
    int Classes,
    int BlankIndex);

internal sealed record OcrTensorDescriptor(
    string InputName,
    string InputType,
    int[] InputShape,
    string OutputName,
    string OutputType,
    int[] OutputShape);

internal sealed record OcrPreprocessingDescriptor(
    int Channels,
    int TargetHeight,
    string WidthMode,
    string ResizeReference,
    string Normalization,
    int CtcBlankIndex);
