namespace RpaFlow.Runtime;

public interface IOneTimeCodeProvider
{
    Task<OneTimeCodeResult> WaitForCodeAsync(
        OneTimeCodeRequest request,
        CancellationToken cancellationToken);
}

public sealed record OneTimeCodeRequest(
    string ProviderAlias,
    DateTimeOffset NotBefore,
    TimeSpan Timeout,
    TimeSpan PollInterval);

public sealed record OneTimeCodeResult(
    string Code,
    DateTimeOffset ReceivedAt);
