using RpaFlow.Playwright.V2;

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

        if (options.Captcha is { } captcha)
        {
            if (captcha.ServiceUrl is { Length: > 0 } url &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != "http" && uri.Scheme != "https") ||
                 !string.IsNullOrEmpty(uri.UserInfo) ||
                 !string.IsNullOrEmpty(uri.Query) ||
                 !string.IsNullOrEmpty(uri.Fragment) ||
                 uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback))
            {
                errors.Add(
                    "Captcha.ServiceUrl deve usar HTTPS, ou HTTP em loopback, sem credenciais, query ou fragmento.");
            }

            if (captcha.ServiceTimeoutSeconds is < 5 or > 600)
            {
                errors.Add("Captcha.ServiceTimeoutSeconds deve estar entre 5 e 600.");
            }

            if (captcha.RecaptchaMaxAttempts is < 1 or > 10)
            {
                errors.Add("Captcha.RecaptchaMaxAttempts deve estar entre 1 e 10.");
            }

            if (captcha.HCaptchaMaxAttempts is < 1 or > 10)
            {
                errors.Add("Captcha.HCaptchaMaxAttempts deve estar entre 1 e 10.");
            }

            if (captcha.HumanHandoffTimeoutSeconds is < 10 or > 86_400)
            {
                errors.Add("Captcha.HumanHandoffTimeoutSeconds deve estar entre 10 e 86400.");
            }

            if (captcha.HumanHandoffPollSeconds is < 1 or > 60)
            {
                errors.Add("Captcha.HumanHandoffPollSeconds deve estar entre 1 e 60.");
            }
            if (captcha.DeadlineSeconds is < 1 or > 600)
            {
                errors.Add("Captcha.DeadlineSeconds deve estar entre 1 e 600.");
            }
            if (captcha.ServiceRetryAttempts is < 1 or > 5)
            {
                errors.Add("Captcha.ServiceRetryAttempts deve estar entre 1 e 5.");
            }
            if (captcha.ServiceRetryBackoffMs is < 0 or > 10_000)
            {
                errors.Add("Captcha.ServiceRetryBackoffMs deve estar entre 0 e 10000.");
            }
            if (captcha.MaximumServiceResponseBytes is < 1_024 or > 16 * 1024 * 1024)
            {
                errors.Add(
                    "Captcha.MaximumServiceResponseBytes deve estar entre 1024 e 16777216.");
            }
            if (captcha.MaximumImagePixels is < 1 or > 16_000_000)
            {
                errors.Add("Captcha.MaximumImagePixels deve estar entre 1 e 16000000.");
            }
            if (!double.IsFinite(captcha.SliderMinimumScore) ||
                captcha.SliderMinimumScore is < 0 or > 1)
            {
                errors.Add("Captcha.SliderMinimumScore deve estar entre 0 e 1.");
            }
            if (captcha.SamePageWaitSeconds is < 1 or > 600)
            {
                errors.Add("Captcha.SamePageWaitSeconds deve estar entre 1 e 600.");
            }
            if (captcha.AllowVlmFallback && string.IsNullOrWhiteSpace(captcha.ServiceUrl))
            {
                errors.Add("Captcha.AllowVlmFallback exige Captcha.ServiceUrl.");
            }
            if (captcha.CloudflareSidecarTimeoutSeconds is < 5 or > 600)
            {
                errors.Add(
                    "Captcha.CloudflareSidecarTimeoutSeconds deve estar entre 5 e 600.");
            }
            if (captcha.CloudflareSidecarMaximumResponseBytes is < 1024 or > 16 * 1024 * 1024)
            {
                errors.Add(
                    "Captcha.CloudflareSidecarMaximumResponseBytes deve estar entre 1024 e 16777216.");
            }
            if (captcha.CloudflareSidecarEnabled)
            {
                if (captcha.CloudflareSidecarProvider?.Trim().ToLowerInvariant() is not
                    ("byparr" or "flaresolverr"))
                {
                    errors.Add(
                        "Captcha.CloudflareSidecarProvider deve ser byparr ou flaresolverr.");
                }
                if (captcha.CloudflareSidecarUrl is not { Length: > 0 } sidecarUrl ||
                    !Uri.TryCreate(sidecarUrl, UriKind.Absolute, out var sidecarUri) ||
                    sidecarUri.Scheme is not ("http" or "https") ||
                    !string.IsNullOrEmpty(sidecarUri.UserInfo) ||
                    !string.IsNullOrEmpty(sidecarUri.Query) ||
                    !string.IsNullOrEmpty(sidecarUri.Fragment) ||
                    sidecarUri.Scheme == Uri.UriSchemeHttp && !sidecarUri.IsLoopback)
                {
                    errors.Add(
                        "Captcha.CloudflareSidecarUrl deve usar HTTPS, ou HTTP em loopback, sem credenciais, query ou fragmento.");
                }
                if (captcha.CloudflareSidecarAllowedHosts is not { Count: > 0 and <= 50 })
                {
                    errors.Add(
                        "Captcha.CloudflareSidecarAllowedHosts deve possuir entre 1 e 50 domínios.");
                }
                else
                {
                    foreach (var host in captcha.CloudflareSidecarAllowedHosts)
                    {
                        try
                        {
                            CloudflareSidecarClient.NormalizeAllowedHost(host);
                        }
                        catch (CaptchaException)
                        {
                            errors.Add(
                                "Captcha.CloudflareSidecarAllowedHosts contém um domínio inválido.");
                            break;
                        }
                    }
                }
                if (captcha.CloudflareSidecarApiKey?.Length > 4096)
                {
                    errors.Add("Captcha.CloudflareSidecarApiKey excede 4096 caracteres.");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Configuração do runtime inválida:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }
}
