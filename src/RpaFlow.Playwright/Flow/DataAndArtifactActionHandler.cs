using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace RpaFlow.Playwright;

internal sealed class DataAndArtifactActionHandler : IFlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fail",
            "transformPath",
            "captureTimestamp",
            "waitForOneTimeCode",
            "screenshot",
            "download",
            "setVariable",
            "readElement",
            "readElements",
            "safeFinalConfirmation"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "fail":
                throw new InvalidOperationException(
                    LegacyFlowValueResolver.ResolveRequired(action, execution.Context.Data));
            case "transformpath":
                LegacyFlowPathTransformer.Execute(action, execution.Context.Data);
                Console.WriteLine(
                    $"  Caminho transformado e armazenado em: {action.Target}.");
                break;
            case "capturetimestamp":
                LegacyOneTimeCodeFlowActionExecutor.CaptureTimestamp(
                    action,
                    execution.Context.Data,
                    execution.Context.TimeProvider);
                Console.WriteLine(
                    $"  Instante UTC armazenado em: {action.Target}.");
                break;
            case "waitforonetimecode":
                await LegacyOneTimeCodeFlowActionExecutor.WaitForOneTimeCodeAsync(
                    action,
                    execution.Context.Data,
                    execution.Context.OneTimeCodeProvider,
                    cancellationToken);
                Console.WriteLine(
                    $"  Código de uso único armazenado em: {action.Target}.");
                break;
            case "screenshot":
                await CaptureScreenshotAsync(action, execution.Context);
                break;
            case "download":
                await FlowDownloadExecutor.ExecuteAsync(
                    action,
                    execution.Context,
                    cancellationToken);
                break;
            case "setvariable":
                SetVariable(action, execution.Context);
                break;
            case "readelement":
                await ReadElementAsync(action, execution, cancellationToken);
                break;
            case "readelements":
                await ReadElementsAsync(action, execution, cancellationToken);
                break;
            case "safefinalconfirmation":
                throw new InvalidOperationException(
                    "safeFinalConfirmation exige uma política histórica específica do sistema de destino.");
            default:
                throw new InvalidOperationException(
                    $"O handler de dados e artefatos não interpreta '{action.Type}'.");
        }
    }

    private static async Task CaptureScreenshotAsync(
        FlowActionDefinition action,
        RpaContext context)
    {
        var fallbackName = action.ScreenshotName ?? "evidencia";
        var destination = LegacyArtifactDestinationResolver.Resolve(
            action,
            context,
            fallbackName);
        var path = await context.Artifacts.CaptureScreenshotAsync(
            fallbackName,
            destination);
        if (!string.IsNullOrWhiteSpace(action.Target))
        {
            context.Data.SetRuntimeValue(action.Target, JsonValue.Create(path));
        }
        Console.WriteLine($"  Evidência salva em: {path}");
    }

    private static void SetVariable(
        FlowActionDefinition action,
        RpaContext context)
    {
        var value = LegacyFlowValueResolver.ResolveNode(action, context.Data);
        context.Data.SetRuntimeValue(action.Target!, value);
        Console.WriteLine($"  Valor armazenado em: {action.Target}.");
    }

    private static async Task ReadElementAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var locator = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleAttachedAsync(locator, action.Name);
        cancellationToken.ThrowIfCancellationRequested();

        var value = await ReadValueAsync(action, locator);

        execution.Context.Data.SetRuntimeValue(action.Target!, value);
        Console.WriteLine($"  Valor do elemento armazenado em: {action.Target}.");
    }

    private static async Task ReadElementsAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var locator = execution.CreateLocator(action);
        var count = await locator.CountAsync();
        var maximum = action.MaxItems ?? 1_000;
        if (count > maximum)
        {
            throw new InvalidOperationException(
                $"A leitura '{action.Name}' encontrou {count} elementos e ultrapassou " +
                $"o limite configurado de {maximum}.");
        }

        var values = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values.Add(await ReadValueAsync(action, locator.Nth(index)));
        }

        execution.Context.Data.SetRuntimeValue(action.Target!, values);
        Console.WriteLine(
            $"  {count} valores de elementos armazenados em: {action.Target}.");
    }

    private static async Task<JsonNode?> ReadValueAsync(
        FlowActionDefinition action,
        ILocator locator) =>
        action.Property?.ToLowerInvariant() switch
        {
            "value" => JsonValue.Create(await locator.InputValueAsync()),
            "text" => JsonValue.Create((await locator.TextContentAsync())?.Trim()),
            "checked" => JsonValue.Create(await locator.IsCheckedAsync()),
            "attribute" => JsonValue.Create(
                await locator.GetAttributeAsync(action.Attribute!)),
            _ => throw new InvalidOperationException(
                $"Propriedade de leitura inválida em '{action.Name}': " +
                $"'{action.Property}'.")
        };
}
