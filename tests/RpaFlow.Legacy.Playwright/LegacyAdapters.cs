using System.Globalization;
using System.Text.Json.Nodes;

namespace RpaFlow.Playwright;

internal static class LegacyFlowValueResolver
{
    public static string ResolveRequired(
        FlowActionDefinition action,
        FlowDataContext data) =>
        FlowValueResolver.ResolveRequired(
            action.Value,
            action.ValueSource,
            $"a ação '{action.Name}'",
            data);

    public static string? ResolveOptional(
        FlowActionDefinition action,
        FlowDataContext data) =>
        FlowValueResolver.ResolveOptional(action.Value, action.ValueSource, data);

    public static JsonNode? ResolveNode(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (FlowValueResolver.HasLiteral(action.Value))
        {
            return FlowValueResolver.ToNode(action.Value);
        }

        if (string.IsNullOrWhiteSpace(action.ValueSource))
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' não informou value nem valueSource.");
        }

        return data.ResolveRequired(action.ValueSource, $"A ação '{action.Name}'");
    }

    public static IReadOnlyList<JsonNode?> ResolveList(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (action.Items is not null)
        {
            return action.Items.Select(FlowValueResolver.ToNode).ToArray();
        }

        var source = action.ItemsSource!;
        var resolved = data.ResolveRequired(source, $"A lista de '{action.Name}'");
        if (resolved is not JsonArray array)
        {
            throw new InvalidOperationException(
                $"O caminho '{source}' da ação '{action.Name}' deve conter uma lista JSON.");
        }

        return array.ToArray();
    }

    public static int ResolveIterationCount(
        FlowActionDefinition action,
        FlowDataContext data)
    {
        if (action.Times is not null)
        {
            return action.Times.Value;
        }

        var node = data.ResolveRequired(
            action.TimesSource!,
            $"A repetição '{action.Name}'");
        var rawValue = FlowValueResolver.ConvertSimpleValue(
            node,
            action.TimesSource!) ?? string.Empty;
        if (!int.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var count) ||
            count is < 0 or > FlowDefinitionValidator.MaximumLoopIterations)
        {
            throw new InvalidOperationException(
                $"A repetição '{action.Name}' exige um inteiro entre 0 e " +
                $"{FlowDefinitionValidator.MaximumLoopIterations}; recebido '{rawValue}'.");
        }

        return count;
    }
}

internal static class LegacyFlowPathTransformer
{
    public static void Execute(FlowActionDefinition action, FlowDataContext data)
    {
        var path = LegacyFlowValueResolver.ResolveRequired(action, data);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' recebeu um caminho vazio.");
        }

        var result = action.Operation?.ToLowerInvariant() switch
        {
            "filename" => GetFileName(path, action.Name),
            "filenamewithoutextension" => GetFileNameWithoutExtension(path, action.Name),
            "extension" => GetExtension(path, action.Name),
            "directoryname" => GetDirectoryName(path),
            _ => throw new InvalidOperationException(
                $"Operação de caminho não interpretada em '{action.Name}': '{action.Operation}'.")
        };

        data.SetRuntimeValue(action.Target!, JsonValue.Create(result));
    }

    private static string GetFileName(string path, string actionName)
    {
        if (path[^1] is '/' or '\\')
        {
            throw new InvalidOperationException(
                $"A ação '{actionName}' exige um caminho de arquivo, mas recebeu uma pasta.");
        }

        var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        var fileName = separator < 0 ? path : path[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException(
                $"A ação '{actionName}' não conseguiu obter o nome do arquivo.");
        }

        return fileName;
    }

    private static string GetFileNameWithoutExtension(string path, string actionName)
    {
        var fileName = GetFileName(path, actionName);
        var extension = LastExtensionIndex(fileName);
        return extension < 0 ? fileName : fileName[..extension];
    }

    private static string GetExtension(string path, string actionName)
    {
        var fileName = GetFileName(path, actionName);
        var extension = LastExtensionIndex(fileName);
        return extension < 0 ? string.Empty : fileName[extension..];
    }

    private static string GetDirectoryName(string path)
    {
        var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        if (separator < 0)
        {
            return string.Empty;
        }

        if (separator == 0 || (separator == 2 && path.Length >= 3 && path[1] == ':'))
        {
            return path[..(separator + 1)];
        }

        return path[..separator];
    }

    private static int LastExtensionIndex(string fileName)
    {
        var index = fileName.LastIndexOf('.');
        return index <= 0 ? -1 : index;
    }
}

internal static class LegacyOneTimeCodeFlowActionExecutor
{
    public static void CaptureTimestamp(
        FlowActionDefinition action,
        FlowDataContext data,
        TimeProvider? timeProvider = null)
    {
        var capturedAt = (timeProvider ?? TimeProvider.System)
            .GetUtcNow()
            .ToUniversalTime();
        data.SetRuntimeValue(
            action.Target!,
            JsonValue.Create(capturedAt.ToString("O", CultureInfo.InvariantCulture)));
    }

    public static async Task WaitForOneTimeCodeAsync(
        FlowActionDefinition action,
        FlowDataContext data,
        IOneTimeCodeProvider? provider,
        CancellationToken cancellationToken)
    {
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

        data.SetRuntimeValue(action.Target!, JsonValue.Create(result.Code));
    }
}

internal static class LegacyArtifactDestinationResolver
{
    public static ArtifactDestination Resolve(
        FlowActionDefinition action,
        RpaContext context,
        string? fallbackFileName = null)
    {
        var directory = FlowValueResolver.ResolveOptionalText(
            action.DestinationDirectory,
            action.DestinationDirectorySource,
            context.Data);
        var fileName = FlowValueResolver.ResolveOptionalText(
            action.FileName,
            action.FileNameSource,
            context.Data) ?? fallbackFileName;
        var conflict = action.ConflictStrategy?.ToLowerInvariant() switch
        {
            null or "" or "unique" => ArtifactConflictStrategy.Unique,
            "fail" => ArtifactConflictStrategy.Fail,
            "overwrite" => ArtifactConflictStrategy.Overwrite,
            _ => throw new InvalidOperationException(
                $"Estratégia de conflito inválida: '{action.ConflictStrategy}'.")
        };

        return new ArtifactDestination(
            directory,
            fileName,
            action.SeparateByExecution ?? true,
            conflict);
    }
}
