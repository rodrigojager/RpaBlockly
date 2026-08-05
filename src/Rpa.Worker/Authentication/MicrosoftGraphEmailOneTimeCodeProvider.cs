using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Rpa.Worker.Configuration;
using RpaFlow.Runtime;

namespace Rpa.Worker.Authentication;

/// <summary>
/// Consulta códigos de uso único em caixas do Microsoft 365 pelo Microsoft Graph.
/// A implementação é somente leitura: não marca, move nem exclui mensagens.
/// </summary>
public sealed class MicrosoftGraphEmailOneTimeCodeProvider : IOneTimeCodeProvider
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _providerLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly MicrosoftGraphEmailReaderOptions _readerOptions;
    private readonly Func<
        EmailOneTimeCodeProviderOptions,
        DateTimeOffset,
        CancellationToken,
        Task<OneTimeCodeResult?>>? _captureOverride;

    public MicrosoftGraphEmailOneTimeCodeProvider(RpaWorkerOptions workerOptions)
    {
        ArgumentNullException.ThrowIfNull(workerOptions);
        _readerOptions = workerOptions.EmailReader;
    }

    internal MicrosoftGraphEmailOneTimeCodeProvider(
        RpaWorkerOptions workerOptions,
        Func<
            EmailOneTimeCodeProviderOptions,
            DateTimeOffset,
            CancellationToken,
            Task<OneTimeCodeResult?>> captureOverride)
        : this(workerOptions)
    {
        _captureOverride = captureOverride ??
            throw new ArgumentNullException(nameof(captureOverride));
    }

    public async Task<OneTimeCodeResult> WaitForCodeAsync(
        OneTimeCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var alias = request.ProviderAlias.Trim();
        if (!_readerOptions.Providers.TryGetValue(alias, out var options))
        {
            throw new InvalidOperationException(
                $"O provider de código de uso único '{alias}' não está configurado.");
        }

        if (!options.Enabled)
        {
            throw new InvalidOperationException(
                $"O provider de código de uso único '{alias}' não está habilitado.");
        }

        using var overallTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        overallTimeout.CancelAfter(request.Timeout);
        var providerLock = _providerLocks.GetOrAdd(
            alias,
            static _ => new SemaphoreSlim(1, 1));
        var lockAcquired = false;
        try
        {
            try
            {
                await providerLock.WaitAsync(overallTimeout.Token);
                lockAcquired = true;
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"O tempo limite para aguardar o provider '{alias}' foi excedido.",
                    exception);
            }

            GraphServiceClient? graphClient = null;
            if (_captureOverride is null)
            {
                var credential = new ClientSecretCredential(
                    _readerOptions.TenantId.Trim(),
                    _readerOptions.ClientId.Trim(),
                    _readerOptions.ClientSecret);
                graphClient = new GraphServiceClient(credential, Scopes);
            }

            while (true)
            {
                OneTimeCodeResult? result;
                try
                {
                    result = _captureOverride is null
                        ? await CaptureOnceAsync(
                            graphClient!,
                            options,
                            request.NotBefore,
                            overallTimeout.Token)
                        : await _captureOverride(
                            options,
                            request.NotBefore,
                            overallTimeout.Token);
                }
                catch (TimeoutException) when (!overallTimeout.IsCancellationRequested)
                {
                    result = null;
                }
                catch (OperationCanceledException exception)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Nenhum código foi encontrado pelo provider '{alias}' dentro do tempo limite.",
                        exception);
                }

                if (result is not null)
                {
                    return result;
                }

                try
                {
                    await Task.Delay(request.PollInterval, overallTimeout.Token);
                }
                catch (OperationCanceledException exception)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Nenhum código foi encontrado pelo provider '{alias}' dentro do tempo limite.",
                        exception);
                }
            }
        }
        finally
        {
            if (lockAcquired)
            {
                providerLock.Release();
            }
        }
    }

    private async Task<OneTimeCodeResult?> CaptureOnceAsync(
        GraphServiceClient graphClient,
        EmailOneTimeCodeProviderOptions options,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var oldestAccepted = now.AddMinutes(-options.MaximumEmailAgeMinutes);
        if (notBefore > oldestAccepted)
        {
            oldestAccepted = notBefore;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_readerOptions.RequestTimeoutSeconds));

        MessageCollectionResponse? response;
        try
        {
            response = await graphClient.Users[options.Mailbox.Trim()]
                .Messages
                .GetAsync(request =>
                {
                    request.QueryParameters.Top = options.RequestedEmailCount;
                    request.QueryParameters.Filter = BuildFilter(
                        oldestAccepted,
                        now,
                        options.SubjectContains);
                    request.QueryParameters.Orderby = ["receivedDateTime DESC"];
                    request.QueryParameters.Select =
                    [
                        "subject",
                        "receivedDateTime",
                        "from",
                        "body",
                        "bodyPreview"
                    ];
                }, timeout.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "A consulta do código de uso único ao Microsoft Graph excedeu o tempo limite.",
                exception);
        }

        var expression = new Regex(
            options.CodePattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return FindNewestMatchingCode(
            response?.Value ?? [],
            expression,
            options,
            oldestAccepted,
            now,
            cancellationToken);
    }

    internal static OneTimeCodeResult? FindNewestMatchingCode(
        IEnumerable<Message> messages,
        Regex expression,
        EmailOneTimeCodeProviderOptions options,
        DateTimeOffset oldestAccepted,
        DateTimeOffset newestAccepted,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages.OrderByDescending(item => item.ReceivedDateTime))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (message.ReceivedDateTime is not { } receivedAt ||
                receivedAt < oldestAccepted ||
                receivedAt > newestAccepted ||
                !ContainsSubject(message.Subject, options.SubjectContains) ||
                !MatchesSender(message.From?.EmailAddress?.Address, options.SenderAddress))
            {
                continue;
            }

            var code = ExtractCode(expression, message.Body?.Content)
                       ?? ExtractCode(expression, message.BodyPreview);
            if (code is not null)
            {
                return new OneTimeCodeResult(code, receivedAt);
            }
        }

        return null;
    }

    internal static string BuildFilter(
        DateTimeOffset oldestAccepted,
        DateTimeOffset newestAccepted,
        string subjectContains)
    {
        var escapedSubject = subjectContains.Trim().Replace("'", "''", StringComparison.Ordinal);
        return $"receivedDateTime ge {FormatDateTime(oldestAccepted)} and " +
               $"receivedDateTime le {FormatDateTime(newestAccepted)} and " +
               $"contains(subject, '{escapedSubject}')";
    }

    internal static string? ExtractCode(Regex expression, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var match = expression.Match(content);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups.Count > 1
            ? match.Groups[1].Value
            : match.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static bool MatchesSender(string? sender, string? expected) =>
        string.IsNullOrWhiteSpace(expected) ||
        sender?.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsSubject(string? subject, string expected) =>
        subject?.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase) == true;

    private static string FormatDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture);
}
