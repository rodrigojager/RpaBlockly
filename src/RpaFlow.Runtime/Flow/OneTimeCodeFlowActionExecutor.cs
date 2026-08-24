using System.Globalization;
using System.Text.Json.Nodes;

namespace RpaFlow.Runtime;

public static class OneTimeCodeFlowActionExecutor
{
    public static void CaptureTimestamp(
        RpaFlow.Contracts.V2.FlowActionDefinition action,
        FlowDataContext data,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(data);
        var capturedAt = (timeProvider ?? TimeProvider.System)
            .GetUtcNow()
            .ToUniversalTime();
        data.SetRuntimeValue(
            action.Output!,
            JsonValue.Create(capturedAt.ToString("O", CultureInfo.InvariantCulture)));
    }

    public static async Task WaitForOneTimeCodeAsync(
        RpaFlow.Contracts.V2.FlowActionDefinition action,
        FlowDataContext data,
        IOneTimeCodeProvider? provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null)
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' exige um IOneTimeCodeProvider configurado no host.");
        }

        var source = action.NotBeforeSource!;
        var sourceValue = data.ResolveRequired(source, $"A ação '{action.Name}'");
        var notBeforeText = FlowValueResolver.ConvertSimpleValue(sourceValue, source);
        if (string.IsNullOrWhiteSpace(notBeforeText) ||
            !DateTimeOffset.TryParseExact(
                notBeforeText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var notBefore))
        {
            throw new InvalidOperationException(
                $"O caminho '{source}' da ação '{action.Name}' deve conter " +
                "uma data e hora no formato round-trip (O).");
        }

        var request = new OneTimeCodeRequest(
            action.ProviderAlias!,
            notBefore,
            TimeSpan.FromMilliseconds(action.TimeoutMs!.Value),
            TimeSpan.FromMilliseconds(action.PollIntervalMs!.Value));
        var result = await provider.WaitForCodeAsync(request, cancellationToken) ??
            throw new InvalidOperationException(
                $"O provider '{action.ProviderAlias}' retornou um resultado nulo.");
        if (string.IsNullOrWhiteSpace(result.Code) || result.ReceivedAt < notBefore)
        {
            throw new InvalidOperationException(
                $"O provider '{action.ProviderAlias}' retornou um código inválido.");
        }

        data.SetRuntimeValue(action.Output!, JsonValue.Create(result.Code));
    }
}
