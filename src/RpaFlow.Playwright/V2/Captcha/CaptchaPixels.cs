namespace RpaFlow.Playwright.V2;

/// <summary>
/// Conversões puras de pixels RGBA (vindos do canvas da página) para os tensores
/// do OCR e do template-matching. Sem nenhuma dependência nativa.
/// </summary>
internal static class CaptchaPixels
{
    public const int MaximumPixels = 16_000_000;
    public const int MaximumTargetWidth = 4_096;

    public static int ValidateRgba(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                $"Dimensões de imagem inválidas: {width}x{height}.");
        }

        int pixelCount;
        int expectedBytes;
        try
        {
            pixelCount = checked(width * height);
            expectedBytes = checked(pixelCount * 4);
        }
        catch (OverflowException exception)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "As dimensões da imagem ultrapassam a aritmética segura.",
                innerException: exception);
        }

        if (pixelCount > MaximumPixels)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                $"A imagem possui {pixelCount} pixels; o limite é {MaximumPixels}.");
        }

        if (rgba.Length != expectedBytes)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.DecodeFailed,
                $"O buffer RGBA tem {rgba.Length} bytes; esperado {expectedBytes} para {width}x{height}.");
        }

        return pixelCount;
    }

    /// <summary>RGBA → cinza (luma Rec.601), 0..255.</summary>
    public static double[] ToGrayscale(
        byte[] rgba,
        int pixelCount,
        bool compositeOnWhite = false)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (pixelCount < 0 || rgba.Length != checked(pixelCount * 4))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "O buffer RGBA não corresponde à quantidade de pixels informada.");
        }

        var gray = new double[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var luma =
                0.299 * rgba[offset] +
                0.587 * rgba[offset + 1] +
                0.114 * rgba[offset + 2];
            if (compositeOnWhite && rgba[offset + 3] < byte.MaxValue)
            {
                var alpha = rgba[offset + 3] / 255.0;
                luma = luma * alpha + 255 * (1 - alpha);
            }

            gray[i] = luma;
        }

        return gray;
    }

    /// <summary>Extrai máscara de opacidade (alpha &gt; 0) do RGBA.</summary>
    public static bool[] ToAlphaMask(byte[] rgba, int pixelCount)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (pixelCount < 0 || rgba.Length != checked(pixelCount * 4))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "O buffer RGBA não corresponde à quantidade de pixels informada.");
        }

        var mask = new bool[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            mask[i] = rgba[i * 4 + 3] > 0;
        }

        return mask;
    }

    /// <summary>
    /// Redimensiona para altura <paramref name="targetHeight"/> mantendo a
    /// proporção, com interpolação bilinear (equivalente prático ao ANTIALIAS).
    /// Devolve (largura, amostras normalizadas em [-1, 1]).
    /// </summary>
    public static (int Width, float[] Samples) ResizeAndNormalize(
        double[] gray,
        int width,
        int height,
        int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetHeight, 1);
        if (gray.Length != checked(width * height))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "A imagem em tons de cinza não corresponde às dimensões informadas.");
        }

        // O ddddocr 1.4.11 usa int(), que trunca a largura positiva.
        var targetWidth = Math.Max(1, (int)(width * (double)targetHeight / height));
        if (targetWidth > MaximumTargetWidth)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                $"A largura normalizada {targetWidth} excede o limite {MaximumTargetWidth}.");
        }

        var samples = new float[checked(targetWidth * targetHeight)];

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = (y + 0.5) * height / (double)targetHeight - 0.5;
            var y0 = (int)Math.Floor(sourceY);
            var y1 = y0 + 1;
            var wy = sourceY - y0;
            y0 = Math.Clamp(y0, 0, height - 1);
            y1 = Math.Clamp(y1, 0, height - 1);

            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = (x + 0.5) * width / (double)targetWidth - 0.5;
                var x0 = (int)Math.Floor(sourceX);
                var x1 = x0 + 1;
                var wx = sourceX - x0;
                x0 = Math.Clamp(x0, 0, width - 1);
                x1 = Math.Clamp(x1, 0, width - 1);

                var top = gray[y0 * width + x0] * (1 - wx) + gray[y0 * width + x1] * wx;
                var bottom = gray[y1 * width + x0] * (1 - wx) + gray[y1 * width + x1] * wx;
                var value = top * (1 - wy) + bottom * wy;

                // normalização idêntica ao ddddocr: (p/255 - 0.5) / 0.5 -> [-1, 1]
                samples[y * targetWidth + x] = (float)(value / 255 - 0.5) / 0.5f;
            }
        }

        return (targetWidth, samples);
    }
}
