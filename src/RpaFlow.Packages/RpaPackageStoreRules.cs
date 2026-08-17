namespace RpaFlow.Packages;

public static class RpaPackageStoreRules
{
    public static void EnsureExpectedRevision(
        string rpaId,
        PackageRevision? current,
        PackageRevision? expected)
    {
        if (current is null && expected is null)
        {
            return;
        }

        if (current is null || expected is null ||
            !current.Value.Equals(expected.Value, StringComparison.Ordinal))
        {
            throw new PackageRevisionConflictException(
                $"A revisão esperada do pacote '{rpaId}' não corresponde à atual. " +
                $"Esperada: '{expected?.Value ?? "<ausente>"}'; " +
                $"atual: '{current?.Value ?? "<ausente>"}'.");
        }
    }

    public static void ValidateRpaId(string rpaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rpaId);
        if (rpaId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException(
                "O ID do RPA aceita somente letras ASCII, números, ponto, hífen e sublinhado.",
                nameof(rpaId));
        }
    }
}
