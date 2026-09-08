namespace RpaFlow.Playwright.V2;

/// <summary>
/// Decodificador CTC greedy no formato exato do ddddocr: ignora índice 0
/// (blank) e remove índices iguais consecutivos. Os índices já chegam como
/// argmax da saída inteira do modelo.
/// </summary>
internal static class CtcDecoder
{
    public static string Decode(long[] indices, string[] charset)
    {
        var builder = new System.Text.StringBuilder();
        var last = 0L;
        foreach (var index in indices)
        {
            if (index == last)
            {
                continue;
            }

            last = index;
            if (index < 0 || index >= charset.Length)
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ModelMismatch,
                    $"A saída OCR contém índice {index} fora do charset de {charset.Length} classes.");
            }

            if (index != 0)
            {
                builder.Append(charset[index]);
            }
        }

        return builder.ToString();
    }
}
