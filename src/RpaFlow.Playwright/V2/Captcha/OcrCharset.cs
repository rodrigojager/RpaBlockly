using System.Text.Json;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Carrega o charset CTC embutido no assembly (8210 classes, índice 0 = blank),
/// espelhando o modelo ddddocr usado pelo OCR embutido.
/// </summary>
internal static class OcrCharset
{
    private const string ResourceName =
        "RpaFlow.Playwright.V2.Captcha.ocr-charset.json";

    private static readonly string[] Symbols = Load();

    public static string[] Get() => Symbols;

    private static string[] Load()
    {
        var assembly = typeof(OcrCharset).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException(
                $"O recurso embutido '{ResourceName}' não foi encontrado.");
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<string[]>(reader.ReadToEnd()) ??
            throw new InvalidOperationException("O charset do OCR está vazio.");
    }
}
