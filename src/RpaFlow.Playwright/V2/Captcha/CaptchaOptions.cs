namespace RpaFlow.Playwright.V2;

/// <summary>
/// Configuração do resolvedor de captchas. O resolvedor embutido (OCR de
/// imagem e slider) roda em processo; os tipos que exigem Python (reCAPTCHA
/// v2 por áudio, grade de imagem) chamam o serviço HTTP opcional.
/// </summary>
public sealed record CaptchaOptions(
    /// <summary>URL base do serviço Python de captchas (opcional).</summary>
    string? ServiceUrl = null,
    /// <summary>Chave enviada como Bearer para o serviço (opcional).</summary>
    string? ServiceApiKey = null,
    /// <summary>Caminho do modelo ONNX (common.onnx) para OCR embutido.</summary>
    string? OcrModelPath = null,
    /// <summary>Timeout de cada chamada ao serviço externo.</summary>
    int ServiceTimeoutSeconds = 60,
    /// <summary>Tentativas de resolução por áudio do reCAPTCHA v2.</summary>
    int RecaptchaMaxAttempts = 3,
    /// <summary>Rodadas de classificação da grade binária do hCaptcha.</summary>
    int HCaptchaMaxAttempts = 3,
    /// <summary>Tempo máximo aguardando intervenção humana.</summary>
    int HumanHandoffTimeoutSeconds = 900,
    /// <summary>Intervalo de polling do sinal de intervenção humana.</summary>
    int HumanHandoffPollSeconds = 2,
    /// <summary>Orçamento total padrão de uma ação de captcha.</summary>
    int DeadlineSeconds = 90,
    /// <summary>Retentativas de transporte do contrato legado; V2 não é reenviado.</summary>
    int ServiceRetryAttempts = 2,
    /// <summary>Atraso base entre retentativas transitórias.</summary>
    int ServiceRetryBackoffMs = 250,
    /// <summary>Limite do corpo de resposta do serviço.</summary>
    int MaximumServiceResponseBytes = 1024 * 1024,
    /// <summary>Limite de imagem decodificada para OCR/matching.</summary>
    int MaximumImagePixels = CaptchaPixels.MaximumPixels,
    /// <summary>Score mínimo de slider que autoriza interação.</summary>
    double SliderMinimumScore = 0.65,
    /// <summary>Política padrão impede backends pagos/remotos não configurados.</summary>
    bool LocalOnly = true,
    /// <summary>Detecção automática pós-navegação permanece opt-in.</summary>
    bool AutoSolveEnabled = false,
    /// <summary>Fallback VLM permanece desabilitado por padrão.</summary>
    bool AllowVlmFallback = false,
    /// <summary>Janela máxima para observar lifecycle same-page antes do handoff.</summary>
    int SamePageWaitSeconds = 30,
    /// <summary>Habilita o fallback isolado para Cloudflare Managed Challenge.</summary>
    bool CloudflareSidecarEnabled = false,
    /// <summary>Provider sidecar: byparr ou flaresolverr.</summary>
    string CloudflareSidecarProvider = "byparr",
    /// <summary>URL base privada do sidecar.</summary>
    string? CloudflareSidecarUrl = null,
    /// <summary>Bearer opcional exigido pelo proxy de proteção do sidecar.</summary>
    string? CloudflareSidecarApiKey = null,
    /// <summary>Timeout da tentativa isolada do sidecar.</summary>
    int CloudflareSidecarTimeoutSeconds = 60,
    /// <summary>Limite do envelope JSON retornado pelo sidecar.</summary>
    int CloudflareSidecarMaximumResponseBytes = 1024 * 1024,
    /// <summary>Domínios-base autorizados para navegação e cookies de clearance.</summary>
    IReadOnlyList<string>? CloudflareSidecarAllowedHosts = null);
