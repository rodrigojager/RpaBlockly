using System.Text.Json.Serialization;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Estados sem ambiguidade de uma tentativa de resolução de captcha.
/// Preencher/clique não equivalem a aceite: Solved só sai da verificação
/// observável feita pelo adapter do navegador.
/// </summary>
public enum CaptchaSolveStatus
{
    /// <summary>Nenhum desafio identificado no escopo observado.</summary>
    NotPresent,
    /// <summary>Inferência produziu texto/coordenadas, ainda sem aceite.</summary>
    AnswerProduced,
    /// <summary>Ações executadas, confirmação ainda pendente.</summary>
    InteractionDone,
    /// <summary>O adapter verificou a pós-condição do desafio.</summary>
    Solved,
    /// <summary>Há desafio/pendência que demanda operador.</summary>
    NeedsHuman,
    /// <summary>Modalidade conhecida sem adapter habilitado.</summary>
    Unsupported,
    /// <summary>Classificação ou resultado inconclusivo.</summary>
    Uncertain,
    /// <summary>Falha final da tentativa, com motivo.</summary>
    Failed,
    /// <summary>Captura, prazo ou execução já encerrados.</summary>
    Expired,
    /// <summary>Execução cancelada pelo chamador.</summary>
    Cancelled
}

/// <summary>Códigos de erro estáveis do pipeline de captcha e do serviço HTTP.</summary>
public static class CaptchaErrorCodes
{
    public const string UnsupportedType = "unsupported_type";
    public const string InvalidPayload = "invalid_payload";
    public const string ModelMissing = "model_missing";
    public const string ModelMismatch = "model_mismatch";
    public const string Busy = "busy";
    public const string DeadlineExceeded = "deadline_exceeded";
    public const string LowConfidence = "low_confidence";
    public const string StaleSnapshot = "stale_snapshot";
    public const string DecodeFailed = "decode_failed";
    public const string UpstreamUnavailable = "upstream_unavailable";
    public const string Cancelled = "cancelled";
    public const string NeedsConfiguration = "needs_configuration";
    public const string VerificationFailed = "verification_failed";
    public const string ExtractionFailed = "extraction_failed";
    public const string ContractViolation = "contract_violation";
    public const string Unauthorized = "unauthorized";
    public const string ResponseTooLarge = "response_too_large";
}

/// <summary>Família/modalidade do desafio detectado.</summary>
public enum CaptchaKind
{
    Unknown,
    ImageText,
    Slider,
    RecaptchaV2,
    RecaptchaV3,
    RecaptchaEnterprise,
    HCaptcha,
    CloudflareTurnstile,
    CloudflareChallenge,
    ArkoseFunCaptcha,
    GeeTest,
    AwsWaf,
    FriendlyCaptcha
}

/// <summary>
/// Desafio capturado: identidade, escopo e evidência da observação. O
/// SnapshotId invalida respostas calculadas sobre uma imagem/widget anterior.
/// </summary>
public sealed record CaptchaChallenge(
    string ChallengeId,
    string? ExecutionId,
    string? ActionId,
    string Provider,
    CaptchaKind Kind,
    string? Variant,
    string? FrameIdentity,
    string? SnapshotId,
    DateTimeOffset CapturedAt,
    CaptchaElementBounds? ElementBounds,
    string? RecipeId,
    bool Visible,
    bool AlreadySolved,
    string Evidence);

/// <summary>Caixa do elemento em pixels CSS do viewport/frame observado.</summary>
public sealed record CaptchaElementBounds(float X, float Y, float Width, float Height);

/// <summary>
/// Ação tipada produzida por um solver. O executor .NET é o único que a
/// converte em interação Playwright; coordenadas são do espaço image-pixels
/// da captura, salvo indicação explícita.
/// </summary>
public sealed record CaptchaPlannedAction(
    CaptchaActionKind Kind,
    string? TargetRole = null,
    string? Text = null,
    float? X = null,
    float? Y = null,
    float? ToX = null,
    float? ToY = null,
    IReadOnlyList<CaptchaPoint>? Points = null);

public sealed record CaptchaPoint(float X, float Y);

public enum CaptchaActionKind
{
    TypeText,
    Click,
    Drag,
    MultiClick,
    HumanHandoff
}

/// <summary>
/// Decisão de um tile de grade binária (hCaptcha) produzida pelo serviço de
/// inferência. O executor .NET clica o elemento correspondente ao Index; a
/// decisão nunca é tratada como aceite do desafio.
/// </summary>
public sealed record CaptchaTileDecision(int Index, bool Match, double? Confidence);

/// <summary>
/// Resultado estruturado de uma tentativa. Nunca relata Solved sem
/// VerificationEvidence preenchida pelo adapter.
/// </summary>
public sealed record CaptchaSolveResult
{
    public required CaptchaSolveStatus Status { get; init; }
    public string? Provider { get; init; }
    public CaptchaKind Kind { get; init; } = CaptchaKind.Unknown;
    public string? SolverId { get; init; }
    public string? ModelVersion { get; init; }
    /// <summary>Só preenchida quando o algoritmo tem medida definida e validada.</summary>
    public double? Confidence { get; init; }
    public IReadOnlyList<CaptchaPlannedAction> Actions { get; init; } = [];
    public string? Answer { get; init; }
    /// <summary>Decisões por tile de grade binária (hCaptcha), quando aplicável.</summary>
    public IReadOnlyList<CaptchaTileDecision>? TileDecisions { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Retryable { get; init; }
    public int Attempts { get; init; } = 1;
    public long ElapsedMs { get; init; }
    public string? VerificationEvidence { get; init; }
    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];

    public static CaptchaSolveResult Failure(
        CaptchaKind kind,
        string errorCode,
        string message,
        bool retryable,
        long elapsedMs,
        string? solverId = null) =>
        new()
        {
            Status = CaptchaSolveStatus.Failed,
            Kind = kind,
            SolverId = solverId,
            ErrorCode = errorCode,
            ErrorMessage = message,
            Retryable = retryable,
            ElapsedMs = elapsedMs
        };
}

/// <summary>
/// Falha tipada do pipeline de captcha: carrega o código estável e a
/// sinalização de retentativa sem depender do texto da mensagem.
/// </summary>
public sealed class CaptchaException : InvalidOperationException
{
    public CaptchaException(
        string errorCode,
        string message,
        bool retryable = false,
        Exception? innerException = null,
        int attempts = 1)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
        Retryable = retryable;
        Attempts = Math.Clamp(attempts, 0, 10);
    }

    public string ErrorCode { get; }

    public bool Retryable { get; }

    public int Attempts { get; }
}
