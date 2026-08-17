using System.Text.Json.Nodes;
using Microsoft.Playwright;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime;
using RpaFlow.Runtime.V2;
using FlowActionDefinition = RpaFlow.Contracts.V2.FlowActionDefinition;

namespace RpaFlow.Playwright.V2;

internal sealed class V2DataAndArtifactActionHandler : IV2FlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fail", "transformPath", "captureTimestamp", "waitForOneTimeCode",
            "screenshot", "download", "setVariable", "readElement", "readElements",
            "safeFinalConfirmation"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "fail":
                throw new InvalidOperationException(
                    V2FlowValueResolver.ResolveRequired(action, execution.Context.Data));
            case "transformpath":
                FlowPathTransformer.Execute(action, execution.Context.Data);
                Console.WriteLine($"  Caminho transformado e armazenado em: {action.Output}.");
                return;
            case "capturetimestamp":
                OneTimeCodeFlowActionExecutor.CaptureTimestamp(
                    action,
                    execution.Context.Data,
                    execution.Context.TimeProvider);
                Console.WriteLine($"  Instante UTC armazenado em: {action.Output}.");
                return;
            case "waitforonetimecode":
                await OneTimeCodeFlowActionExecutor.WaitForOneTimeCodeAsync(
                    action,
                    execution.Context.Data,
                    execution.Context.OneTimeCodeProvider,
                    cancellationToken);
                Console.WriteLine($"  Código de uso único armazenado em: {action.Output}.");
                return;
            case "screenshot":
                await CaptureScreenshotAsync(action, execution, cancellationToken);
                return;
            case "download":
                await V2FlowDownloadExecutor.ExecuteAsync(
                    action,
                    execution,
                    cancellationToken);
                return;
            case "setvariable":
                SetVariable(action, execution.Context);
                return;
            case "readelement":
                await ReadElementAsync(action, execution, cancellationToken);
                return;
            case "readelements":
                await ReadElementsAsync(action, execution, cancellationToken);
                return;
            case "safefinalconfirmation":
                throw new InvalidOperationException(
                    "safeFinalConfirmation exige uma política específica do sistema de destino. " +
                    "A V2 genérica não cria nem generaliza esse contrato local.");
            default:
                throw new InvalidOperationException(
                    $"O handler V2 de dados e artefatos não interpreta '{action.Type}'.");
        }
    }

    private static async Task CaptureScreenshotAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var fallbackName = action.ScreenshotName ?? "evidencia";
        var destination = ArtifactDestinationResolver.Resolve(
            action,
            context,
            fallbackName);
        string path;
        if (action.Target is null)
        {
            path = await context.Artifacts.CaptureScreenshotAsync(
                fallbackName,
                destination);
        }
        else
        {
            var target = await execution.ResolveTargetAsync(
                action,
                LocatorRequiredState.Visible,
                cancellationToken);
            path = await context.Artifacts.CaptureElementScreenshotAsync(
                target.Locator,
                fallbackName,
                destination);
        }

        if (!string.IsNullOrWhiteSpace(action.Output))
        {
            context.Data.SetRuntimeValue(action.Output, JsonValue.Create(path));
        }

        Console.WriteLine($"  Evidência salva em: {path}");
    }

    private static void SetVariable(FlowActionDefinition action, RpaContext context)
    {
        var value = V2FlowValueResolver.ResolveNode(action, context.Data);
        context.Data.SetRuntimeValue(action.Output!, value);
        Console.WriteLine($"  Valor armazenado em: {action.Output}.");
    }

    private static async Task ReadElementAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var target = await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Attached,
            cancellationToken);
        var value = await ReadValueAsync(action, target.Locator);
        execution.Context.Data.SetRuntimeValue(action.Output!, value);
        Console.WriteLine($"  Valor do elemento armazenado em: {action.Output}.");
    }

    private static async Task ReadElementsAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var target = await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Attached,
            cancellationToken);
        var count = await target.Locator.CountAsync();
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
            values.Add(await ReadValueAsync(action, target.Locator.Nth(index)));
        }

        execution.Context.Data.SetRuntimeValue(action.Output!, values);
        Console.WriteLine(
            $"  {count} valores de elementos armazenados em: {action.Output}.");
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
                $"Propriedade de leitura inválida em '{action.Name}': '{action.Property}'.")
        };
}
