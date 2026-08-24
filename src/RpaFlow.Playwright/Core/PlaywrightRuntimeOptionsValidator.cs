namespace RpaFlow.Playwright;

public static class PlaywrightRuntimeOptionsValidator
{
    public static void Validate(PlaywrightRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        if (!PlaywrightBrowserSelection.IsSupported(options.Browser))
        {
            errors.Add(
                $"Browser não é suportado: '{options.Browser}'. Valores aceitos: " +
                PlaywrightBrowserSelection.SupportedValuesDescription + ".");
        }

        if (options.ActionTimeoutSeconds is < 1 or > 600)
        {
            errors.Add("ActionTimeoutSeconds deve estar entre 1 e 600.");
        }

        if (options.UploadTimeoutSeconds is < 1 or > 3_600)
        {
            errors.Add("UploadTimeoutSeconds deve estar entre 1 e 3600.");
        }

        if (options.ReadinessQuietPeriodMs is < 50 or > 60_000)
        {
            errors.Add("ReadinessQuietPeriodMs deve estar entre 50 e 60000.");
        }

        if (options.FormStabilityMs is < 50 or > 60_000)
        {
            errors.Add("FormStabilityMs deve estar entre 50 e 60000.");
        }

        if (options.BusySelectors is { Count: > 50 } ||
            options.BusySelectors?.Any(string.IsNullOrWhiteSpace) == true)
        {
            errors.Add(
                "BusySelectors deve possuir no máximo 50 seletores CSS não vazios.");
        }

        if (options.HoldBrowserOpenForInspection && options.Headless)
        {
            errors.Add(
                "HoldBrowserOpenForInspection exige Headless=false para manter uma janela visível.");
        }

        if (options.MaximumArtifactBytes is < 1_024 or > 1_073_741_824)
        {
            errors.Add("MaximumArtifactBytes deve estar entre 1024 e 1073741824.");
        }

        if (options.MaximumArtifactFilesPerExecution is < 1 or > 10_000)
        {
            errors.Add("MaximumArtifactFilesPerExecution deve estar entre 1 e 10000.");
        }

        if (options.ArtifactRetentionDays is < 1 or > 3_650)
        {
            errors.Add("ArtifactRetentionDays deve estar entre 1 e 3650.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            errors.Add("OutputDirectory é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(options.ConfigurationDirectory) ||
            !Directory.Exists(options.ConfigurationDirectory))
        {
            errors.Add("ConfigurationDirectory deve apontar para uma pasta existente.");
        }

        if (options.ViewportWidth is < 320 or > 10_000 ||
            options.ViewportHeight is < 240 or > 10_000)
        {
            errors.Add("O viewport configurado está fora dos limites suportados.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Configuração do runtime inválida:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }
}
