using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public static class FlowDownloadExecutor
{
    public static Task ExecuteAsync(
        FlowActionDefinition action,
        RpaContext context,
        CancellationToken cancellationToken) =>
        action.DownloadMode?.ToLowerInvariant() switch
        {
            "click" => DownloadByClickAsync(action, context, cancellationToken),
            "request" => DownloadByRequestAsync(action, context, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Modo de download inválido em '{action.Name}': '{action.DownloadMode}'.")
        };

    private static async Task DownloadByClickAsync(
        FlowActionDefinition action,
        RpaContext context,
        CancellationToken cancellationToken)
    {
        var locator = FlowLocatorFactory.Create(
            context.Page,
            action,
            context.Data);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        cancellationToken.ThrowIfCancellationRequested();

        var download = await context.Page.RunAndWaitForDownloadAsync(
            () => locator.ClickAsync(),
            new PageRunAndWaitForDownloadOptions
            {
                Timeout = action.TimeoutMs ??
                    context.Options.ActionTimeoutSeconds * 1_000
            });
        var destination = ArtifactDestinationResolver.Resolve(action, context);
        var path = await context.Artifacts.SaveDownloadAsync(download, destination);
        StoreResult(action, context, path);
    }

    private static async Task DownloadByRequestAsync(
        FlowActionDefinition action,
        RpaContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuredUrl = FlowValueResolver.ResolveRequired(action, context.Data);
        var url = Uri.TryCreate(configuredUrl, UriKind.Absolute, out var absoluteUrl)
            ? absoluteUrl.ToString()
            : new Uri(new Uri(context.Page.Url), configuredUrl).ToString();
        var headers = ResolveHeaders(action, context);
        var options = new APIRequestContextOptions
        {
            Method = action.Method?.ToUpperInvariant() ?? "GET",
            Timeout = action.TimeoutMs ??
                context.Options.ActionTimeoutSeconds * 1_000,
            Headers = headers
        };
        AddRequestBody(action, context, options, headers);

        var response = await context.Page.Context.APIRequest.FetchAsync(url, options);
        if (!response.Ok)
        {
            throw new InvalidOperationException(
                $"O download por requisição retornou HTTP {response.Status} " +
                $"({response.StatusText}) em '{action.Name}'.");
        }

        var contents = await response.BodyAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var suggestedFileName = ResolveResponseFileName(response);
        var destination = ArtifactDestinationResolver.Resolve(action, context);
        var path = await context.Artifacts.SaveBytesAsync(
            contents,
            suggestedFileName,
            destination,
            cancellationToken);
        StoreResult(action, context, path);
    }

    private static Dictionary<string, string> ResolveHeaders(
        FlowActionDefinition action,
        RpaContext context)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!FlowValueResolver.HasLiteral(action.RequestHeaders) &&
            string.IsNullOrWhiteSpace(action.RequestHeadersSource))
        {
            return headers;
        }

        var node = FlowValueResolver.ResolveNodeOptional(
            action.RequestHeaders,
            action.RequestHeadersSource,
            context.Data);
        if (node is not JsonObject headerObject)
        {
            throw new InvalidOperationException(
                $"Os cabeçalhos de '{action.Name}' devem formar um objeto JSON.");
        }

        foreach (var header in headerObject)
        {
            headers[header.Key] = FlowValueResolver.ConvertSimpleValue(
                header.Value,
                $"cabeçalho {header.Key}") ?? string.Empty;
        }

        return headers;
    }

    private static void AddRequestBody(
        FlowActionDefinition action,
        RpaContext context,
        APIRequestContextOptions options,
        IDictionary<string, string> headers)
    {
        var hasBody = FlowValueResolver.HasLiteral(action.RequestBody) ||
            !string.IsNullOrWhiteSpace(action.RequestBodySource);
        if (!hasBody)
        {
            return;
        }

        var body = FlowValueResolver.ResolveNodeOptional(
            action.RequestBody,
            action.RequestBodySource,
            context.Data);
        switch (action.BodyType?.ToLowerInvariant() ?? "json")
        {
            case "json":
                options.DataString = body?.ToJsonString() ?? "null";
                headers.TryAdd("Content-Type", "application/json");
                break;
            case "text":
                options.DataString = FlowValueResolver.ConvertSimpleValue(
                    body,
                    action.RequestBodySource ?? "requestBody") ?? string.Empty;
                headers.TryAdd("Content-Type", "text/plain; charset=utf-8");
                break;
            case "form":
                if (body is not JsonObject formObject)
                {
                    throw new InvalidOperationException(
                        $"O corpo form de '{action.Name}' deve ser um objeto JSON simples.");
                }

                var form = context.Page.Context.APIRequest.CreateFormData();
                foreach (var field in formObject)
                {
                    form.Set(
                        field.Key,
                        FlowValueResolver.ConvertSimpleValue(
                            field.Value,
                            $"campo {field.Key}") ?? string.Empty);
                }

                options.Form = form;
                break;
            default:
                throw new InvalidOperationException(
                    $"Tipo de corpo inválido em '{action.Name}': '{action.BodyType}'.");
        }
    }

    private static string ResolveResponseFileName(IAPIResponse response)
    {
        if (response.Headers.TryGetValue("content-disposition", out var rawDisposition) &&
            ContentDispositionHeaderValue.TryParse(rawDisposition, out var disposition))
        {
            var headerFileName = disposition.FileNameStar ?? disposition.FileName;
            if (!string.IsNullOrWhiteSpace(headerFileName))
            {
                return headerFileName.Trim().Trim('"');
            }
        }

        if (Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri))
        {
            var urlFileName = Uri.UnescapeDataString(Path.GetFileName(responseUri.LocalPath));
            if (!string.IsNullOrWhiteSpace(urlFileName))
            {
                return urlFileName;
            }
        }

        return $"download-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.bin";
    }

    private static void StoreResult(
        FlowActionDefinition action,
        RpaContext context,
        string path)
    {
        if (!string.IsNullOrWhiteSpace(action.Target))
        {
            context.Data.SetRuntimeValue(action.Target, JsonValue.Create(path));
        }

        Console.WriteLine($"  Arquivo baixado e salvo em: {path}");
    }

}
