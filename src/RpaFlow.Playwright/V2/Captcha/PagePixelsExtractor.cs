using System.Text.Json;
using Microsoft.Playwright;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Extrai pixels RGBA crus de um elemento (img/canvas/elemento qualquer) via
/// canvas da página, evitando decodificação de PNG em .NET. O mesmo esquema
/// serve para OCR embutido e para o slider.
/// </summary>
internal static class PagePixelsExtractor
{
    public const string Script = """
        (element, maxPixels) => {
          if (!element) return null;
          const supported = element instanceof HTMLImageElement ||
            element instanceof HTMLCanvasElement ||
            element instanceof HTMLVideoElement;
          if (!supported) return null;
          const canvas = document.createElement('canvas');
          let width = 0, height = 0;
          if (element instanceof HTMLImageElement) {
            width = element.naturalWidth; height = element.naturalHeight;
          } else if (element instanceof HTMLCanvasElement) {
            width = element.width; height = element.height;
          } else {
            const rect = element.getBoundingClientRect();
            width = Math.round(rect.width); height = Math.round(rect.height);
          }
          if (!width || !height || width * height > maxPixels) return null;
          canvas.width = width; canvas.height = height;
          const context = canvas.getContext('2d');
          let data;
          try {
            context.drawImage(element, 0, 0);
            data = context.getImageData(0, 0, width, height).data;
          } catch (error) {
            return null;
          }
          const bytes = new Uint8Array(data);
          let binary = '';
          const chunkSize = 0x8000;
          for (let i = 0; i < bytes.length; i += chunkSize) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
          }
          return { width: width, height: height, rgba: btoa(binary) };
        }
        """;

    private const string ScreenshotScript = """
        async (element, args) => {
          const image = new Image();
          image.src = `data:image/png;base64,${args.pngBase64}`;
          try { await image.decode(); } catch { return null; }
          const width = image.naturalWidth;
          const height = image.naturalHeight;
          if (!width || !height || width * height > args.maxPixels) return null;
          const canvas = document.createElement('canvas');
          canvas.width = width; canvas.height = height;
          const context = canvas.getContext('2d');
          context.drawImage(image, 0, 0);
          const data = context.getImageData(0, 0, width, height).data;
          const bytes = new Uint8Array(data);
          let binary = '';
          const chunkSize = 0x8000;
          for (let i = 0; i < bytes.length; i += chunkSize) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
          }
          return { width, height, rgba: btoa(binary) };
        }
        """;

    public static async Task<PixelFrame?> TryExtractAsync(
        ILocator locator,
        CancellationToken cancellationToken,
        int maximumPixels = CaptchaPixels.MaximumPixels)
    {
        ArgumentNullException.ThrowIfNull(locator);
        if (maximumPixels is < 1 or > CaptchaPixels.MaximumPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await locator.EvaluateAsync<JsonElement>(Script, maximumPixels);
        cancellationToken.ThrowIfCancellationRequested();
        var direct = Parse(result, maximumPixels, "canvas");
        if (direct is not null)
        {
            return direct;
        }

        var screenshot = await locator.ScreenshotAsync(
            new LocatorScreenshotOptions { Type = ScreenshotType.Png });
        cancellationToken.ThrowIfCancellationRequested();
        var screenshotResult = await locator.EvaluateAsync<JsonElement>(
            ScreenshotScript,
            new
            {
                pngBase64 = Convert.ToBase64String(screenshot),
                maxPixels = maximumPixels
            });
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(screenshotResult, maximumPixels, "screenshot");
    }

    private static PixelFrame? Parse(JsonElement result, int maximumPixels, string source)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("rgba", out var rgba) ||
            rgba.ValueKind != JsonValueKind.String ||
            !result.TryGetProperty("width", out var widthProperty) ||
            !result.TryGetProperty("height", out var heightProperty) ||
            !widthProperty.TryGetInt32(out var width) ||
            !heightProperty.TryGetInt32(out var height))
        {
            return null;
        }

        if (width <= 0 || height <= 0 ||
            (long)width * height > maximumPixels)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                $"A captura {width}x{height} excede o limite de pixels.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(rgba.GetString() ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.DecodeFailed,
                "A captura retornou RGBA base64 inválido.",
                innerException: exception);
        }

        _ = CaptchaPixels.ValidateRgba(bytes, width, height);
        return new PixelFrame(width, height, bytes, source);
    }
}

internal sealed record PixelFrame(int Width, int Height, byte[] Rgba, string Source = "canvas");
