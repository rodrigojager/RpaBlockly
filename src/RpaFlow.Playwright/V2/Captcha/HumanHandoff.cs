using System.Text.Json;
using System.Text.Json.Serialization;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// Transporte por arquivos para intervenção humana. Cada tentativa tem IDs
/// completos e requestId próprio, impedindo que uma resposta antiga ou de outra
/// execução retome o fluxo atual.
/// </summary>
internal static class HumanHandoff
{
    public const string FolderName = "human-handoff";
    public const string RequestFileName = "human-handoff.request.json";
    public const string ResponseFileName = "human-handoff.response.json";
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<HumanHandoffResponse> ExecuteAsync(
        HumanHandoffContext context,
        string message,
        string outputDirectory,
        TimeSpan timeout,
        TimeSpan pollInterval,
        IReadOnlyList<string>? evidenceReferences,
        CancellationToken cancellationToken,
        Func<HumanHandoffRequest, CancellationToken, ValueTask>? onRequested = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ValidateDuration(timeout, nameof(timeout));
        ValidateDuration(pollInterval, nameof(pollInterval));

        var executionId = RequirePathSegment(context.ExecutionId, nameof(context.ExecutionId));
        var actionId = RequirePathSegment(context.ActionId, nameof(context.ActionId));
        var challengeId = RequirePathSegment(context.ChallengeId, nameof(context.ChallengeId));
        var folder = Path.Combine(
            Path.GetFullPath(outputDirectory),
            FolderName,
            executionId,
            actionId,
            challengeId);
        Directory.CreateDirectory(folder);
        var requestPath = Path.Combine(folder, RequestFileName);
        var responsePath = Path.Combine(folder, ResponseFileName);
        var requestedAt = DateTimeOffset.UtcNow;
        var request = new HumanHandoffRequest(
            ContractVersion: 2,
            RequestId: Guid.NewGuid().ToString("N"),
            executionId,
            actionId,
            challengeId,
            context.Provider,
            context.Kind,
            message,
            State: "Pending",
            InteractionMode: "sameBrowserSession",
            RequestedAtUtc: requestedAt,
            ExpiresAtUtc: requestedAt.Add(timeout),
            ResponseFile: ResponseFileName,
            NormalizeEvidenceReferences(outputDirectory, evidenceReferences));
        await WriteAtomicallyAsync(requestPath, request, cancellationToken);
        if (onRequested is not null)
        {
            try
            {
                await onRequested(request, cancellationToken);
            }
            catch
            {
                await WriteAtomicallyAsync(
                    requestPath,
                    request with { State = "Cancelled" },
                    CancellationToken.None);
                throw;
            }
        }

        Console.WriteLine("  Intervenção humana solicitada.");
        Console.WriteLine($"    {message}");
        Console.WriteLine($"    Solicitação: {requestPath}");

        try
        {
            while (DateTimeOffset.UtcNow < request.ExpiresAtUtc)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await TryReadResponseAsync(responsePath, cancellationToken);
                if (response is not null && Matches(request, response))
                {
                    if (response.RespondedAtUtc < request.RequestedAtUtc ||
                        response.RespondedAtUtc > request.ExpiresAtUtc)
                    {
                        await Task.Delay(pollInterval, cancellationToken);
                        continue;
                    }

                    if (response.Action.Equals("continue", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteAtomicallyAsync(
                            requestPath,
                            request with { State = "Acknowledged" },
                            cancellationToken);
                        Console.WriteLine("  Intervenção humana confirmada; retomando.");
                        return response;
                    }
                    if (response.Action.Equals("reject", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteAtomicallyAsync(
                            requestPath,
                            request with { State = "Rejected" },
                            cancellationToken);
                        throw new HumanHandoffRejectedException(
                            "O operador rejeitou a retomada da intervenção humana.");
                    }
                }

                await Task.Delay(pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            await WriteAtomicallyAsync(
                requestPath,
                request with { State = "Cancelled" },
                CancellationToken.None);
            throw;
        }

        await WriteAtomicallyAsync(
            requestPath,
            request with { State = "Expired" },
            CancellationToken.None);
        throw new TimeoutException(
            $"Intervenção humana não concluída em {timeout.TotalSeconds:N0} segundos.");
    }

    private static async Task<HumanHandoffResponse?> TryReadResponseAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > MaximumResponseBytes)
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<HumanHandoffResponse>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    private static bool Matches(
        HumanHandoffRequest request,
        HumanHandoffResponse response) =>
        response.ContractVersion == request.ContractVersion &&
        string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal) &&
        string.Equals(response.ExecutionId, request.ExecutionId, StringComparison.Ordinal) &&
        string.Equals(response.ActionId, request.ActionId, StringComparison.Ordinal) &&
        string.Equals(response.ChallengeId, request.ChallengeId, StringComparison.Ordinal);

    private static async Task WriteAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(value, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static IReadOnlyList<string> NormalizeEvidenceReferences(
        string outputDirectory,
        IReadOnlyList<string>? references)
    {
        if (references is null || references.Count == 0)
        {
            return [];
        }

        var root = Path.GetFullPath(outputDirectory);
        var normalized = new List<string>(references.Count);
        foreach (var reference in references)
        {
            var fullPath = Path.GetFullPath(reference);
            var relative = Path.GetRelativePath(root, fullPath);
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }
            normalized.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
        }
        return normalized;
    }

    private static string RequirePathSegment(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value is "." or ".." ||
            value.Length > 128 ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"{name} não é um identificador seguro.", name);
        }
        return value;
    }

    private static void ValidateDuration(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

internal sealed record HumanHandoffContext(
    string ExecutionId,
    string ActionId,
    string ChallengeId,
    string? Provider,
    CaptchaKind Kind);

internal sealed record HumanHandoffRequest(
    int ContractVersion,
    string RequestId,
    string ExecutionId,
    string ActionId,
    string ChallengeId,
    string? Provider,
    CaptchaKind Kind,
    string Message,
    string State,
    string InteractionMode,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string ResponseFile,
    IReadOnlyList<string> EvidenceReferences);

internal sealed record HumanHandoffResponse(
    int ContractVersion,
    string RequestId,
    string ExecutionId,
    string ActionId,
    string ChallengeId,
    string Action,
    DateTimeOffset RespondedAtUtc);

internal sealed class HumanHandoffRejectedException(string message) :
    InvalidOperationException(message);
