namespace RpaFlow.Playwright.V2;

/// <summary>
/// Template matching normalizado em image-pixels. Não executa interação e não
/// declara suporte universal a famílias de widgets.
/// </summary>
internal static class SliderSolver
{
    private const long MaximumPixelComparisons = 100_000_000;

    public static SliderMatch FindBestMatch(
        double[] backgroundGray,
        int backgroundWidth,
        int backgroundHeight,
        double[] pieceGray,
        bool[] pieceMask,
        int pieceWidth,
        int pieceHeight,
        int? maximumVerticalOffset = null,
        CancellationToken cancellationToken = default)
    {
        Validate(
            backgroundGray,
            backgroundWidth,
            backgroundHeight,
            pieceGray,
            pieceMask,
            pieceWidth,
            pieceHeight);

        var maskedPixels = pieceMask.Count(value => value);
        if (maskedPixels < 8)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "A peça do slider não possui pixels opacos suficientes.");
        }

        var maxY = backgroundHeight - pieceHeight;
        if (maximumVerticalOffset is not null)
        {
            maxY = Math.Min(maxY, Math.Max(0, maximumVerticalOffset.Value));
        }
        var candidateColumns = backgroundWidth - pieceWidth + 1;
        var candidateRows = maxY + 1;
        var comparisons = checked((long)candidateColumns * candidateRows * maskedPixels);
        if (comparisons > MaximumPixelComparisons)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                $"O slider exigiria {comparisons:N0} comparações de pixels; " +
                $"o limite é {MaximumPixelComparisons:N0}.");
        }

        var bestX = -1;
        var bestY = -1;
        var bestScore = double.NegativeInfinity;
        for (var candidateY = 0; candidateY <= maxY; candidateY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var candidateX = 0; candidateX <= backgroundWidth - pieceWidth; candidateX++)
            {
                if ((candidateX & 63) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var score = Score(
                    backgroundGray,
                    backgroundWidth,
                    pieceGray,
                    pieceMask,
                    pieceWidth,
                    pieceHeight,
                    candidateX,
                    candidateY,
                    maskedPixels,
                    cancellationToken);
                if (double.IsFinite(score) && score > bestScore)
                {
                    bestScore = score;
                    bestX = candidateX;
                    bestY = candidateY;
                }
            }
        }

        if (bestX < 0 || bestY < 0 || !double.IsFinite(bestScore))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.LowConfidence,
                "O slider não produziu casamento com variância válida.");
        }

        return new SliderMatch(bestX, bestY, bestScore, maskedPixels);
    }

    /// <summary>Compatibilidade dos checks históricos de slider horizontal.</summary>
    public static (int BestX, double Score) FindBestOffset(
        double[] backgroundGray,
        int backgroundWidth,
        int backgroundHeight,
        double[] pieceGray,
        bool[] pieceMask,
        int pieceWidth,
        int pieceHeight)
    {
        var match = FindBestMatch(
            backgroundGray,
            backgroundWidth,
            backgroundHeight,
            pieceGray,
            pieceMask,
            pieceWidth,
            pieceHeight,
            maximumVerticalOffset: 0);
        return (match.BestX, match.Score);
    }

    private static double Score(
        double[] background,
        int backgroundWidth,
        double[] piece,
        bool[] mask,
        int pieceWidth,
        int pieceHeight,
        int offsetX,
        int offsetY,
        int count,
        CancellationToken cancellationToken)
    {
        double sumBackground = 0;
        double sumPiece = 0;
        double sumBackgroundSquared = 0;
        double sumPieceSquared = 0;
        double sumProduct = 0;
        for (var y = 0; y < pieceHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < pieceWidth; x++)
            {
                var pieceIndex = y * pieceWidth + x;
                if (!mask[pieceIndex])
                {
                    continue;
                }

                var backgroundIndex =
                    (offsetY + y) * backgroundWidth + offsetX + x;
                var backgroundValue = background[backgroundIndex];
                var pieceValue = piece[pieceIndex];
                sumBackground += backgroundValue;
                sumPiece += pieceValue;
                sumBackgroundSquared += backgroundValue * backgroundValue;
                sumPieceSquared += pieceValue * pieceValue;
                sumProduct += backgroundValue * pieceValue;
            }
        }

        var meanBackground = sumBackground / count;
        var meanPiece = sumPiece / count;
        var varianceBackground =
            sumBackgroundSquared / count - meanBackground * meanBackground;
        var variancePiece = sumPieceSquared / count - meanPiece * meanPiece;
        var varianceProduct = varianceBackground * variancePiece;
        if (varianceProduct <= double.Epsilon)
        {
            return double.NaN;
        }

        var covariance = sumProduct / count - meanBackground * meanPiece;
        return covariance / Math.Sqrt(varianceProduct);
    }

    private static void Validate(
        double[] background,
        int backgroundWidth,
        int backgroundHeight,
        double[] piece,
        bool[] mask,
        int pieceWidth,
        int pieceHeight)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(mask);
        if (backgroundWidth <= 0 || backgroundHeight <= 0 ||
            pieceWidth <= 0 || pieceHeight <= 0 ||
            pieceWidth > backgroundWidth || pieceHeight > backgroundHeight ||
            background.Length != checked(backgroundWidth * backgroundHeight) ||
            piece.Length != checked(pieceWidth * pieceHeight) ||
            mask.Length != piece.Length)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "As dimensões do fundo, peça ou máscara do slider são inválidas.");
        }
        if (background.Any(value => !double.IsFinite(value)) ||
            piece.Any(value => !double.IsFinite(value)))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "Os pixels do slider contêm valores não finitos.");
        }
    }
}

internal sealed record SliderMatch(
    int BestX,
    int BestY,
    double Score,
    int ComparedPixels);
