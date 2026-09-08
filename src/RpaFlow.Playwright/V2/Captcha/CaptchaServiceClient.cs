using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Cliente defensivo do serviço de inferência. Compartilha pool HTTP, limita a
/// resposta e preserva o contrato legado enquanto oferece o envelope V2.
/// </summary>
internal sealed class CaptchaServiceClient : IDisposable
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly CaptchaOptions _options;
    private readonly Uri _solveUri;

    public CaptchaServiceClient(CaptchaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "Captcha.ServiceUrl deve usar HTTPS, ou HTTP em loopback, sem credenciais, query ou fragmento.");
        }
        if (options.ServiceTimeoutSeconds <= 0 ||
            options.MaximumServiceResponseBytes <= 0 ||
            options.ServiceRetryAttempts is < 1 or > 5 ||
            options.ServiceRetryBackoffMs is < 0 or > 10_000)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "A configuração de timeout, retries ou limite de resposta do serviço é inválida.");
        }

        _options = options;
        _solveUri = new Uri(baseUri.ToString().TrimEnd('/') + "/solve", UriKind.Absolute);
    }

    /// <summary>Contrato legado: devolve apenas o texto produzido.</summary>
    public async Task<string> SolveAsync(
        string type,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?>(payload, StringComparer.Ordinal)
        {
            ["type"] = type
        };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.ServiceTimeoutSeconds);
        using var json = await SendAsync(request, deadline, cancellationToken);
        return ReadLegacyResult(json);
    }

    public async Task<CaptchaSolveResult> SolveV2Async(
        CaptchaServiceSolveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeadlineUtc <= DateTimeOffset.UtcNow)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.DeadlineExceeded,
                "O prazo da requisição de captcha já expirou.",
                attempts: 0);
        }

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contractVersion"] = 2,
            ["requestId"] = request.RequestId,
            ["challengeId"] = request.ChallengeId,
            ["snapshotId"] = request.SnapshotId,
            ["type"] = request.Type,
            ["assets"] = request.Assets,
            ["geometry"] = request.Geometry,
            ["hint"] = request.Hint,
            ["provider"] = request.Provider ?? "generic",
            ["variant"] = request.Variant,
            ["task"] = request.Task,
            ["deadlineUtc"] = request.DeadlineUtc,
            ["options"] = request.Options
        };
        using var json = await SendAsync(body, request.DeadlineUtc, cancellationToken);
        return ReadV2Result(json, request);
    }

    private async Task<JsonDocument> SendAsync(
        IReadOnlyDictionary<string, object?> body,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.Serialize(body);
        var v2Request = body.TryGetValue("contractVersion", out var contractVersion) &&
            contractVersion is 2;
        Exception? lastException = null;
        // Sem deduplicação server-side, repetir um POST V2 pode consumir novamente
        // todo o orçamento quando apenas a resposta anterior foi perdida.
        var attempts = v2Request ? 1 : Math.Max(1, _options.ServiceRetryAttempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.DeadlineExceeded,
                    "O serviço de captcha ultrapassou o prazo original.",
                    innerException: lastException,
                    attempts: attempt == 1 ? 0 : 1);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Min(
                remaining,
                TimeSpan.FromSeconds(_options.ServiceTimeoutSeconds)));
            using var request = new HttpRequestMessage(HttpMethod.Post, _solveUri)
            {
                Content = new StringContent(serialized, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(_options.ServiceApiKey))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _options.ServiceApiKey);
            }

            try
            {
                using var response = await SharedHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                var bytes = await ReadLimitedAsync(
                    response.Content,
                    _options.MaximumServiceResponseBytes,
                    timeout.Token);
                var json = ParseJson(bytes, response.StatusCode);
                if (response.IsSuccessStatusCode)
                {
                    return json;
                }

                CaptchaException failure;
                try
                {
                    failure = v2Request
                        ? IsV2Envelope(json.RootElement)
                            ? ReadV2HttpFailure(json.RootElement, body)
                            : new CaptchaException(
                                CaptchaErrorCodes.ContractViolation,
                                "O serviço respondeu à requisição V2 sem envelope V2 correlacionado.",
                                attempts: 0)
                        : ReadFailure(response.StatusCode, json);
                }
                finally
                {
                    json.Dispose();
                }
                lastException = failure;
                if (!failure.Retryable || attempt == attempts || v2Request)
                {
                    throw failure;
                }

                await DelayBeforeRetryAsync(response, attempt, deadline, cancellationToken);
                continue;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                lastException = exception;
                if (attempt == attempts)
                {
                    throw new CaptchaException(
                        CaptchaErrorCodes.DeadlineExceeded,
                        "A chamada ao serviço de captcha excedeu o prazo.",
                        retryable: true,
                        innerException: exception,
                        attempts: 0);
                }
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
                if (attempt == attempts)
                {
                    throw new CaptchaException(
                        CaptchaErrorCodes.UpstreamUnavailable,
                        "O serviço de captcha está indisponível.",
                        retryable: true,
                        innerException: exception,
                        attempts: 0);
                }
            }

            await DelayBeforeRetryAsync(null, attempt, deadline, cancellationToken);
        }

        throw new CaptchaException(
            CaptchaErrorCodes.UpstreamUnavailable,
            "O serviço de captcha não concluiu a inferência.",
            retryable: true,
            innerException: lastException,
            attempts: 0);
    }

    private static string ReadLegacyResult(JsonDocument json)
    {
        var root = RequireObject(json.RootElement);
        if (!root.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.True)
        {
            throw ReadFailure(HttpStatusCode.UnprocessableEntity, json);
        }
        if (!root.TryGetProperty("text", out var text) ||
            text.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(text.GetString()))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O serviço declarou sucesso sem texto válido.");
        }

        return text.GetString()!;
    }

    private static CaptchaSolveResult ReadV2Result(
        JsonDocument json,
        CaptchaServiceSolveRequest request)
    {
        var root = RequireObject(json.RootElement);
        var attempts = ReadReportedAttempts(root);
        try
        {
            return ReadV2Result(root, request, attempts);
        }
        catch (CaptchaException exception) when (exception.Attempts != attempts)
        {
            throw WithAttempts(exception, attempts);
        }
    }

    private static CaptchaSolveResult ReadV2Result(
        JsonElement root,
        CaptchaServiceSolveRequest request,
        int attempts)
    {
        if (!TryReadInt(root, "contractVersion", out var contractVersion) ||
            contractVersion != 2 ||
            !ReadRequiredString(root, "requestId").Equals(request.RequestId, StringComparison.Ordinal) ||
            !ReadRequiredString(root, "challengeId").Equals(request.ChallengeId, StringComparison.Ordinal))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O envelope V2 não correlaciona contrato, requestId ou challengeId.");
        }
        if (!ReadRequiredString(root, "snapshotId").Equals(request.SnapshotId, StringComparison.Ordinal))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.StaleSnapshot,
                "A resposta pertence a um snapshot diferente do desafio atual.");
        }

        var status = ReadRequiredString(root, "status");
        if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            throw ValidateAttemptBudget(ReadV2Failure(root), request.Options);
        }
        if (!status.Equals("AnswerProduced", StringComparison.OrdinalIgnoreCase))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                $"O serviço retornou status V2 inválido para inferência: '{status}'.");
        }
        if (attempts == 0)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O serviço V2 declarou sucesso sem consumir tentativa.",
                attempts: 0);
        }

        var answer = ReadOptionalString(root, "answer");
        var tileDecisions = TryReadTileDecisions(root, request);
        var actions = ReadActions(root, allowEmpty: tileDecisions is not null);
        if (actions.Count == 0 && tileDecisions is null)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O serviço V2 produziu resposta sem ação tipada.");
        }
        if (answer is not null)
        {
            var textActions = actions
                .Where(action => action.Kind == CaptchaActionKind.TypeText)
                .ToArray();
            if (textActions.Length != 1 ||
                !string.Equals(textActions[0].TargetRole, "response", StringComparison.Ordinal) ||
                !string.Equals(textActions[0].Text, answer, StringComparison.Ordinal))
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    "A resposta textual V2 não corresponde a uma única ação TypeText para response.");
            }
        }

        ValidateAttemptBudget(attempts, request.Options);

        return new CaptchaSolveResult
        {
            Status = CaptchaSolveStatus.AnswerProduced,
            Provider = request.Provider,
            Kind = request.Kind ?? ServiceTypeToKind(request.Type),
            SolverId = ReadOptionalString(root, "solver"),
            ModelVersion = ReadOptionalString(root, "modelVersion"),
            Confidence = ReadOptionalDouble(root, "confidence"),
            Actions = actions,
            Answer = answer,
            TileDecisions = tileDecisions,
            Attempts = attempts,
            ElapsedMs = TryReadInt(root, "elapsedMs", out var elapsed) ? elapsed : 0
        };
    }

    private static IReadOnlyList<CaptchaTileDecision>? TryReadTileDecisions(
        JsonElement root,
        CaptchaServiceSolveRequest request)
    {
        if (!root.TryGetProperty("tiles", out var tiles) ||
            tiles.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (tiles.ValueKind != JsonValueKind.Array ||
            tiles.GetArrayLength() is < 1 or > 64)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O campo tiles do serviço V2 é inválido.");
        }
        if (!request.Assets.TryGetValue("tilesBase64", out var sent) ||
            sent is not IReadOnlyCollection<string> sentTiles ||
            sentTiles.Count != tiles.GetArrayLength())
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "A quantidade de tiles do serviço diverge da requisição.");
        }

        var result = new List<CaptchaTileDecision>(tiles.GetArrayLength());
        var seen = new HashSet<int>();
        foreach (var item in tiles.EnumerateArray())
        {
            var value = RequireObject(item);
            if (!TryReadInt(value, "index", out var index) ||
                index < 0 ||
                index >= sentTiles.Count ||
                !seen.Add(index) ||
                !value.TryGetProperty("match", out var match) ||
                match.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    "Uma decisão de tile do serviço V2 é inválida.");
            }
            var confidence = ReadOptionalDouble(value, "confidence");
            if (confidence is < 0 or > 1)
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    "A confiança de um tile do serviço V2 é inválida.");
            }
            result.Add(new CaptchaTileDecision(
                index,
                match.ValueKind == JsonValueKind.True,
                confidence));
        }
        return result;
    }

    private static IReadOnlyList<CaptchaPlannedAction> ReadActions(
        JsonElement root,
        bool allowEmpty = false)
    {
        if (!root.TryGetProperty("actions", out var actions) ||
            actions.ValueKind != JsonValueKind.Array ||
            actions.GetArrayLength() is < 1 or > 100)
        {
            if (allowEmpty &&
                root.TryGetProperty("actions", out var optional) &&
                optional.ValueKind == JsonValueKind.Array &&
                optional.GetArrayLength() == 0)
            {
                return [];
            }
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O campo actions do serviço V2 é inválido.");
        }

        var result = new List<CaptchaPlannedAction>(actions.GetArrayLength());
        foreach (var item in actions.EnumerateArray())
        {
            var value = RequireObject(item);
            var kindText = ReadRequiredString(value, "kind");
            var kind = kindText.ToLowerInvariant() switch
            {
                "typetext" => CaptchaActionKind.TypeText,
                "click" => CaptchaActionKind.Click,
                "drag" => CaptchaActionKind.Drag,
                _ => throw new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    $"A ação V2 possui tipo não permitido: '{kindText}'.")
            };
            if (kind == CaptchaActionKind.TypeText &&
                !string.Equals(ReadOptionalString(value, "targetRole"), "response", StringComparison.Ordinal))
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    "A ação TypeText V2 deve apontar para targetRole=response.");
            }

            var targetRole = ReadOptionalString(value, "targetRole");
            var text = ReadOptionalString(value, "text");
            var x = ReadOptionalSingle(value, "x");
            var y = ReadOptionalSingle(value, "y");
            var toX = ReadOptionalSingle(value, "toX");
            var toY = ReadOptionalSingle(value, "toY");
            if (kind == CaptchaActionKind.TypeText && string.IsNullOrWhiteSpace(text) ||
                kind == CaptchaActionKind.Click && (x is null || y is null) ||
                kind == CaptchaActionKind.Drag &&
                    (x is null || y is null || toX is null || toY is null) ||
                !AreFinite(x, y, toX, toY))
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    $"A ação V2 '{kind}' não contém argumentos válidos.");
            }

            result.Add(new CaptchaPlannedAction(
                kind,
                targetRole,
                text,
                x,
                y,
                toX,
                toY));
        }

        return result;
    }

    private static CaptchaException ReadV2Failure(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) ||
            error.ValueKind != JsonValueKind.Object)
        {
            return new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O serviço V2 declarou falha sem objeto error.");
        }

        var code = ReadRequiredString(error, "code");
        var message = ReadRequiredString(error, "message");
        var retryable = error.TryGetProperty("retryable", out var retry) &&
            retry.ValueKind == JsonValueKind.True;
        var attempts = ReadReportedAttempts(root);
        return new CaptchaException(
            code,
            $"O serviço de captcha falhou: {message}",
            retryable,
            attempts: attempts);
    }

    private static CaptchaException ReadV2HttpFailure(
        JsonElement root,
        IReadOnlyDictionary<string, object?> request)
    {
        var attempts = ReadReportedAttempts(root);
        foreach (var property in new[] { "requestId", "challengeId", "snapshotId" })
        {
            if (!request.TryGetValue(property, out var expected) || expected is not string value ||
                !string.Equals(ReadOptionalString(root, property), value, StringComparison.Ordinal))
            {
                return new CaptchaException(
                    CaptchaErrorCodes.ContractViolation,
                    $"O erro V2 não correlaciona o campo '{property}' com a requisição.",
                    attempts: attempts);
            }
        }
        try
        {
            var failure = WithAttempts(ReadV2Failure(root), attempts);
            return ValidateAttemptBudget(failure, request["options"]);
        }
        catch (CaptchaException exception)
        {
            return WithAttempts(exception, attempts);
        }
    }

    private static int ReadReportedAttempts(JsonElement root)
    {
        if (!root.TryGetProperty("attempts", out _))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O serviço V2 não informou a quantidade de tentativas.",
                attempts: 0);
        }
        if (!TryReadInt(root, "attempts", out var attempts) || attempts is < 0 or > 10)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "O serviço V2 retornou quantidade de tentativas inválida.",
                attempts: Math.Clamp(attempts, 0, 10));
        }
        return attempts;
    }

    private static CaptchaException WithAttempts(
        CaptchaException exception,
        int attempts) =>
        new(
            exception.ErrorCode,
            exception.Message,
            exception.Retryable,
            exception,
            attempts);

    private static CaptchaException ValidateAttemptBudget(
        CaptchaException failure,
        object? options)
    {
        ValidateAttemptBudget(failure.Attempts, options);
        return failure;
    }

    private static void ValidateAttemptBudget(int attempts, object? options)
    {
        var maximumAttempts = 1;
        if (options is IReadOnlyDictionary<string, object?> values &&
            values.TryGetValue("maxAttempts", out var configured))
        {
            maximumAttempts = configured switch
            {
                int number => number,
                long number when number is >= 1 and <= 10 => (int)number,
                JsonElement value when value.TryGetInt32(out var number) => number,
                _ => 1
            };
        }
        maximumAttempts = Math.Clamp(maximumAttempts, 1, 10);
        if (attempts > maximumAttempts)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                $"O serviço V2 declarou {attempts} tentativas para um orçamento de {maximumAttempts}.",
                attempts: attempts);
        }
    }

    private static CaptchaException ReadFailure(HttpStatusCode status, JsonDocument json)
    {
        var root = RequireObject(json.RootElement);
        var code = ReadOptionalString(root, "error") ?? StatusToErrorCode(status);
        var message = ReadOptionalString(root, "message") ?? code;
        return new CaptchaException(
            code,
            $"O serviço de captcha falhou (HTTP {(int)status}): {message}.",
            status is HttpStatusCode.TooManyRequests or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout);
    }

    private static bool IsV2Envelope(JsonElement root) =>
        TryReadInt(root, "contractVersion", out var version) && version == 2;

    private static JsonDocument ParseJson(byte[] bytes, HttpStatusCode status)
    {
        try
        {
            var json = JsonDocument.Parse(bytes);
            _ = RequireObject(json.RootElement);
            return json;
        }
        catch (JsonException exception)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                $"O serviço devolveu JSON inválido (HTTP {(int)status}).",
                innerException: exception);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ResponseTooLarge,
                $"A resposta do serviço excede {maximumBytes} bytes.");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }
            if (output.Length + read > maximumBytes)
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ResponseTooLarge,
                    $"A resposta do serviço excede {maximumBytes} bytes.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private async Task DelayBeforeRetryAsync(
        HttpResponseMessage? response,
        int attempt,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta ??
            TimeSpan.FromMilliseconds(_options.ServiceRetryBackoffMs * attempt);
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero || delay >= remaining)
        {
            return;
        }

        await Task.Delay(delay, cancellationToken);
    }

    private static string StatusToErrorCode(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or
            HttpStatusCode.UnprocessableEntity => CaptchaErrorCodes.InvalidPayload,
        HttpStatusCode.Unauthorized => CaptchaErrorCodes.Unauthorized,
        HttpStatusCode.TooManyRequests => CaptchaErrorCodes.Busy,
        HttpStatusCode.GatewayTimeout => CaptchaErrorCodes.DeadlineExceeded,
        HttpStatusCode.ServiceUnavailable => CaptchaErrorCodes.UpstreamUnavailable,
        _ => CaptchaErrorCodes.UpstreamUnavailable
    };

    private static CaptchaKind ServiceTypeToKind(string type) => type switch
    {
        "image" => CaptchaKind.ImageText,
        "recaptcha_v2_audio" => CaptchaKind.RecaptchaV2,
        "hcaptcha_image_label" => CaptchaKind.HCaptcha,
        _ => CaptchaKind.Unknown
    };

    private static JsonElement RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                "A resposta do serviço deve ser um objeto JSON.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement root, string name) =>
        ReadOptionalString(root, name) ??
        throw new CaptchaException(
            CaptchaErrorCodes.ContractViolation,
            $"A resposta do serviço não contém string '{name}'.");

    private static string? ReadOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadOptionalDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? number
            : null;

    private static float? ReadOptionalSingle(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetSingle(out var number)
            ? number
            : null;

    private static bool AreFinite(params float?[] values) =>
        values.All(value => value is null || float.IsFinite(value.Value));

    private static bool TryReadInt(JsonElement root, string name, out int result)
    {
        if (root.TryGetProperty(name, out var value) && value.TryGetInt32(out result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) =>
        first <= second ? first : second;

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 16,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    // O pool é global; Dispose preserva compatibilidade sem encerrá-lo por ação.
    public void Dispose()
    {
    }
}

internal sealed record CaptchaServiceSolveRequest(
    string RequestId,
    string ChallengeId,
    string SnapshotId,
    string Type,
    IReadOnlyDictionary<string, object?> Assets,
    IReadOnlyDictionary<string, object?> Geometry,
    string? Hint,
    DateTimeOffset DeadlineUtc,
    IReadOnlyDictionary<string, object?> Options,
    string? Provider = null,
    string? Variant = null,
    string? Task = null,
    CaptchaKind? Kind = null);
