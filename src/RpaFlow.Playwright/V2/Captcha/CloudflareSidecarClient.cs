using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using PlaywrightCookie = Microsoft.Playwright.Cookie;

namespace RpaFlow.Playwright.V2;

internal sealed record CloudflareSidecarSolution(
    string Provider,
    string Version,
    Uri FinalUrl,
    string UserAgent,
    bool UserAgentMatched,
    IReadOnlyList<PlaywrightCookie> Cookies,
    long ElapsedMs);

/// <summary>
/// Cliente mínimo dos contratos nativos Byparr/FlareSolverr. Ele transfere
/// somente cf_clearance; HTML, screenshots, headers e demais cookies são ignorados.
/// </summary>
internal sealed class CloudflareSidecarClient : IDisposable
{
    private readonly CaptchaOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _provider;
    private readonly string[] _allowedHosts;

    public CloudflareSidecarClient(CaptchaOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _provider = options.CloudflareSidecarProvider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (_provider is not ("byparr" or "flaresolverr"))
        {
            throw Configuration("CloudflareSidecarProvider deve ser byparr ou flaresolverr.");
        }
        if (!Uri.TryCreate(options.CloudflareSidecarUrl, UriKind.Absolute, out var baseUri))
        {
            throw Configuration("CloudflareSidecarUrl não é uma URL absoluta.");
        }
        _endpoint = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/v1", UriKind.Absolute);
        _allowedHosts = (options.CloudflareSidecarAllowedHosts ?? [])
            .Select(NormalizeAllowedHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_allowedHosts.Length == 0)
        {
            throw Configuration("CloudflareSidecarAllowedHosts exige ao menos um domínio.");
        }

        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                MaxConnectionsPerServer = 1,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            })
            : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        if (!string.IsNullOrWhiteSpace(options.CloudflareSidecarApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", options.CloudflareSidecarApiKey.Trim());
        }
    }

    public async Task<CloudflareSidecarSolution> SolveAsync(
        Uri target,
        string currentUserAgent,
        CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        if (string.IsNullOrWhiteSpace(currentUserAgent) || currentUserAgent.Length > 1024)
        {
            throw Configuration("O User-Agent da sessão Playwright é inválido.");
        }

        var stopwatch = Stopwatch.StartNew();
        var payload = _provider == "byparr"
            ? new Dictionary<string, object?>
            {
                ["cmd"] = "request.get",
                ["url"] = target.AbsoluteUri,
                ["maxTimeout"] = checked(_options.CloudflareSidecarTimeoutSeconds * 1000),
                ["blockMedia"] = false,
                ["returnOnlyCookies"] = true
            }
            : new Dictionary<string, object?>
            {
                ["cmd"] = "request.get",
                ["url"] = target.AbsoluteUri,
                ["maxTimeout"] = checked(_options.CloudflareSidecarTimeoutSeconds * 1000),
                ["returnOnlyCookies"] = true,
                ["disableMedia"] = false
            };
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.CloudflareSidecarTimeoutSeconds));

        try
        {
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw Contract("O endpoint do sidecar tentou redirecionar a requisição.");
            }
            var bytes = await ReadBoundedAsync(response, deadline.Token);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = 32,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            if (!response.IsSuccessStatusCode)
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.UpstreamUnavailable,
                    $"O sidecar {_provider} recusou a tentativa (HTTP {(int)response.StatusCode}).",
                    retryable: (int)response.StatusCode is 408 or 429 or >= 500,
                    attempts: 1);
            }

            return Parse(document.RootElement, target, currentUserAgent, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.DeadlineExceeded,
                $"O sidecar {_provider} ultrapassou o prazo configurado.",
                retryable: true,
                exception,
                attempts: 1);
        }
        catch (HttpRequestException exception)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.UpstreamUnavailable,
                $"O sidecar {_provider} está indisponível.",
                retryable: true,
                exception,
                attempts: 1);
        }
        catch (JsonException exception)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ContractViolation,
                $"O sidecar {_provider} devolveu JSON inválido.",
                innerException: exception,
                attempts: 1);
        }
    }

    private CloudflareSidecarSolution Parse(
        JsonElement root,
        Uri target,
        string currentUserAgent,
        long elapsedMs)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String ||
            !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("solution", out var solution) ||
            solution.ValueKind != JsonValueKind.Object)
        {
            throw Contract($"O sidecar {_provider} devolveu um envelope incompatível.");
        }

        var version = ReadRequiredText(root, "version", 64);
        var finalUrl = ReadRequiredUri(solution, "url");
        ValidateTarget(finalUrl);
        if (!solution.TryGetProperty("status", out var solutionStatus) ||
            solutionStatus.ValueKind != JsonValueKind.Number ||
            !solutionStatus.TryGetInt32(out var httpStatus) ||
            httpStatus is < 100 or > 599)
        {
            throw Contract("O sidecar não devolveu um status HTTP válido para a navegação.");
        }
        var userAgent = ReadRequiredText(solution, "userAgent", 1024);
        if (!solution.TryGetProperty("cookies", out var cookiesElement) ||
            cookiesElement.ValueKind != JsonValueKind.Array ||
            cookiesElement.GetArrayLength() > 128)
        {
            throw Contract("O sidecar não devolveu uma lista de cookies válida.");
        }

        var cookies = new List<PlaywrightCookie>(1);
        foreach (var item in cookiesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String)
            {
                throw Contract("O sidecar devolveu um cookie inválido.");
            }
            if (!string.Equals(nameElement.GetString(), "cf_clearance", StringComparison.Ordinal))
            {
                continue;
            }
            cookies.Add(NormalizeClearanceCookie(item, target));
        }
        if (cookies.Count != 1)
        {
            throw Contract("O sidecar deve devolver exatamente um cookie cf_clearance válido.");
        }

        return new CloudflareSidecarSolution(
            _provider,
            version,
            finalUrl,
            userAgent,
            string.Equals(userAgent, currentUserAgent, StringComparison.Ordinal),
            cookies,
            elapsedMs);
    }

    private PlaywrightCookie NormalizeClearanceCookie(JsonElement item, Uri target)
    {
        var value = ReadRequiredText(item, "value", 4096);
        var cookieDomain = ReadRequiredText(item, "domain", 253).ToLowerInvariant();
        var domain = cookieDomain.TrimStart('.');
        if (!HostWithin(target.IdnHost, domain) ||
            !_allowedHosts.Any(allowed => HostWithin(domain, allowed)))
        {
            throw Contract("O domínio do cf_clearance não pertence ao alvo autorizado.");
        }
        var path = ReadOptionalText(item, "path", 1024) ?? "/";
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            throw Contract("O path do cf_clearance é inválido.");
        }
        var secure = ReadOptionalBoolean(item, "secure") ?? true;
        if (target.Scheme == Uri.UriSchemeHttps && !secure)
        {
            throw Contract("O sidecar devolveu cf_clearance sem o atributo Secure.");
        }

        var expires = ReadExpiration(item);
        return new PlaywrightCookie
        {
            Name = "cf_clearance",
            Value = value,
            Domain = cookieDomain,
            Path = path,
            Expires = expires,
            HttpOnly = ReadOptionalBoolean(item, "httpOnly") ?? false,
            Secure = secure,
            SameSite = ReadSameSite(item)
        };
    }

    private async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var maximum = _options.CloudflareSidecarMaximumResponseBytes;
        if (response.Content.Headers.ContentLength is { } declared && declared > maximum)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ResponseTooLarge,
                "A resposta do sidecar excedeu o limite configurado.",
                attempts: 1);
        }
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream(Math.Min(maximum, 16 * 1024));
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return target.ToArray();
            }
            if (target.Length + read > maximum)
            {
                throw new CaptchaException(
                    CaptchaErrorCodes.ResponseTooLarge,
                    "A resposta do sidecar excedeu o limite configurado.",
                    attempts: 1);
            }
            target.Write(buffer, 0, read);
        }
    }

    private void ValidateTarget(Uri target)
    {
        if (!target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(target.UserInfo) ||
            IPAddress.TryParse(target.IdnHost, out _) ||
            target.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            !_allowedHosts.Any(allowed => HostWithin(target.IdnHost, allowed)))
        {
            throw new CaptchaException(
                CaptchaErrorCodes.InvalidPayload,
                "O alvo do sidecar deve usar HTTPS e pertencer à allowlist de domínios.",
                attempts: 0);
        }
    }

    internal static string NormalizeAllowedHost(string value)
    {
        var normalized = value?.Trim().TrimStart('.').ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is < 1 or > 253 ||
            normalized.Contains('*') ||
            normalized.Contains('/') ||
            normalized.Contains(':') ||
            IPAddress.TryParse(normalized, out _) ||
            Uri.CheckHostName(normalized) != UriHostNameType.Dns ||
            normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw Configuration("CloudflareSidecarAllowedHosts contém um domínio inválido.");
        }
        return new IdnMapping().GetAscii(normalized);
    }

    internal static bool HostWithin(string host, string allowedBase) =>
        host.Equals(allowedBase, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + allowedBase, StringComparison.OrdinalIgnoreCase);

    private static string ReadRequiredText(JsonElement owner, string name, int maximum)
    {
        var value = ReadOptionalText(owner, name, maximum);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Contract($"O sidecar não informou {name}.");
        }
        return value;
    }

    private static string? ReadOptionalText(JsonElement owner, string name, int maximum)
    {
        if (!owner.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (property.ValueKind != JsonValueKind.String || property.GetString() is not { } value ||
            value.Length > maximum)
        {
            throw Contract($"O campo {name} do sidecar é inválido.");
        }
        return value;
    }

    private static Uri ReadRequiredUri(JsonElement owner, string name)
    {
        var value = ReadRequiredText(owner, name, 4096);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw Contract($"O campo {name} do sidecar não é uma URL absoluta.");
        }
        return uri;
    }

    private static bool? ReadOptionalBoolean(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Contract($"O campo {name} do sidecar é inválido.");
        }
        return property.GetBoolean();
    }

    private static float? ReadExpiration(JsonElement item)
    {
        JsonElement property;
        if (!item.TryGetProperty("expires", out property) &&
            !item.TryGetProperty("expiry", out property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            throw Contract("A expiração do cf_clearance é inválida.");
        }
        if (value <= 0)
        {
            return null;
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (value < now - 300 || value > now + TimeSpan.FromDays(7).TotalSeconds)
        {
            throw Contract("A expiração do cf_clearance está fora da janela permitida.");
        }
        return checked((float)value);
    }

    private static SameSiteAttribute? ReadSameSite(JsonElement item)
    {
        var value = ReadOptionalText(item, "sameSite", 16);
        return value?.ToLowerInvariant() switch
        {
            null or "" => null,
            "strict" => SameSiteAttribute.Strict,
            "lax" => SameSiteAttribute.Lax,
            "none" or "no_restriction" => SameSiteAttribute.None,
            _ => throw Contract("O atributo SameSite do cf_clearance é inválido.")
        };
    }

    private static CaptchaException Configuration(string message) =>
        new(CaptchaErrorCodes.NeedsConfiguration, message, attempts: 0);

    private static CaptchaException Contract(string message) =>
        new(CaptchaErrorCodes.ContractViolation, message, attempts: 1);

    public void Dispose() => _http.Dispose();
}
