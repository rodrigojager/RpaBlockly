using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RpaFlow.Contracts.Recorder;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;

namespace RpaFlow.Editor.Recorder;

internal sealed partial class RecorderPackageMerger
{
    public RecorderMergeResult Merge(
        RpaPackageDocuments current,
        InspectedRecorderBundle importedBundle,
        RecorderImportApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(importedBundle);
        ArgumentNullException.ThrowIfNull(request);
        ValidateResolvedIssues(importedBundle.Issues, request.ResolvedIssueIds ?? []);

        var imported = Clone(importedBundle.Package);
        ApplyDataMappings(imported, request, importedBundle.Manifest);
        if (request.Mode == RecorderImportMode.Replace)
        {
            RpaPackageValidator.Validate(imported);
            return new RecorderMergeResult(imported, new Dictionary<string, string>(), []);
        }

        var destination = Clone(current);
        var remappings = new Dictionary<string, string>(StringComparer.Ordinal);
        RemapLocators(destination, imported, importedBundle.Manifest.BundleId, request, remappings);
        RemapCandidateIds(destination, imported, importedBundle.Manifest.BundleId, request, remappings);
        RemapActionIds(destination, imported, importedBundle.Manifest.BundleId, request, remappings);
        RemapSubflows(destination, imported, importedBundle.Manifest.BundleId, request, remappings);
        MergeInputs(destination.Flow.Inputs, imported.Flow.Inputs);
        destination.Locators.Locators.AddRange(imported.Locators.Locators);

        switch (request.Mode)
        {
            case RecorderImportMode.AppendMain:
                destination.Flow.Actions.AddRange(imported.Flow.Actions);
                foreach (var subflow in imported.Flow.Subflows)
                {
                    destination.Flow.Subflows.Add(subflow.Key, subflow.Value);
                }
                break;
            case RecorderImportMode.Subflow:
                var requestedName = request.SubflowName?.Trim();
                if (string.IsNullOrWhiteSpace(requestedName) || !SubflowNamePattern().IsMatch(requestedName))
                {
                    throw new InvalidOperationException(
                        "Importação como subflow exige um nome válido e explícito.");
                }
                var finalName = EnsureUnique(
                    requestedName,
                    destination.Flow.Subflows.Keys.Concat(imported.Flow.Subflows.Keys),
                    importedBundle.Manifest.BundleId,
                    "subflow",
                    request.RemapConflicts,
                    remappings);
                destination.Flow.Subflows.Add(finalName, imported.Flow.Actions);
                foreach (var subflow in imported.Flow.Subflows)
                {
                    destination.Flow.Subflows.Add(subflow.Key, subflow.Value);
                }
                break;
            default:
                throw new InvalidOperationException("Modo de importação inválido.");
        }

        RpaPackageValidator.Validate(destination);
        return new RecorderMergeResult(
            destination,
            remappings,
            ["A política do pacote aberto foi preservada; o Recorder não altera resiliência no merge."]);
    }

    public IReadOnlyList<RecorderImportConflict> FindConflicts(
        RpaPackageDocuments current,
        RpaPackageDocuments imported)
    {
        var result = new List<RecorderImportConflict>();
        AddCollisions(
            "LOCATOR_ID",
            current.Locators.Locators.Select(item => item.Id),
            imported.Locators.Locators.Select(item => item.Id),
            "locators",
            result);
        AddCollisions(
            "ACTION_ID",
            EnumerateActions(current.Flow).Select(item => item.Id),
            EnumerateActions(imported.Flow).Select(item => item.Id),
            "actions",
            result);
        AddCollisions(
            "INPUT_PATH",
            current.Flow.Inputs.Select(item => item.Path),
            imported.Flow.Inputs.Select(item => item.Path),
            "inputs",
            result);
        AddCollisions(
            "SUBFLOW_NAME",
            current.Flow.Subflows.Keys,
            imported.Flow.Subflows.Keys,
            "subflows",
            result);
        if (!V2JsonSerializer.Serialize(current.Policy)
                .Equals(V2JsonSerializer.Serialize(imported.Policy), StringComparison.Ordinal))
        {
            result.Add(new RecorderImportConflict(
                "POLICY_DIFFERENCE",
                "rpa.policy.json",
                current.Policy.LocatorResilience.Mode.ToString(),
                imported.Policy.LocatorResilience.Mode.ToString(),
                "replace usa a política importada; merges preservam a política aberta",
                false));
        }
        return result.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> RecordedInputs(RpaPackageDocuments package) =>
        package.Flow.Inputs.Select(item => item.Path)
            .Where(path => path.StartsWith("input.recorded.", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> SecretReferences(RpaPackageDocuments package) =>
        EnumerateSources(package)
            .Where(path => path.StartsWith("secret.recorded.", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> AttachmentReferences(RpaPackageDocuments package) =>
        EnumerateSources(package)
            .Concat(package.Flow.Inputs.Select(input => input.Path))
            .Where(path => path.StartsWith("attachments.recorded.", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static void ApplyDataMappings(
        RpaPackageDocuments imported,
        RecorderImportApplyRequest request,
        RecorderBundleManifest manifest)
    {
        var inputMappings = NormalizeMappings(
            request.InputMappings,
            RecordedInputs(imported),
            "input",
            target => target.StartsWith("input.", StringComparison.Ordinal));
        var secretMappings = NormalizeMappings(
            request.SecretMappings,
            SecretReferences(imported),
            "segredo",
            target => target.StartsWith("input.", StringComparison.Ordinal) ||
                      target.StartsWith("config.", StringComparison.Ordinal));
        var attachmentMappings = NormalizeMappings(
            request.AttachmentMappings,
            AttachmentReferences(imported),
            "attachment",
            target => target.StartsWith("attachments.", StringComparison.Ordinal));
        if (manifest.HasSecrets && secretMappings.Count == 0)
        {
            throw new InvalidOperationException(
                "O bundle contém segredos e exige remapeamento explícito no backend.");
        }
        var mappings = inputMappings.Concat(secretMappings).Concat(attachmentMappings)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var input in imported.Flow.Inputs)
        {
            if (mappings.TryGetValue(input.Path, out var mapped)) input.Path = mapped;
        }
        foreach (var secret in secretMappings.Values.Where(path =>
                     path.StartsWith("input.", StringComparison.Ordinal)))
        {
            if (imported.Flow.Inputs.All(input =>
                    !input.Path.Equals(secret, StringComparison.OrdinalIgnoreCase)))
            {
                imported.Flow.Inputs.Add(new FlowInputRequirementDefinition
                {
                    Path = secret,
                    Type = "string",
                    Required = true
                });
            }
        }
        foreach (var action in EnumerateActions(imported.Flow))
        {
            action.ValueSource = Map(action.ValueSource, mappings);
            action.NotBeforeSource = Map(action.NotBeforeSource, mappings);
            action.DestinationDirectorySource = Map(action.DestinationDirectorySource, mappings);
            action.FileNameSource = Map(action.FileNameSource, mappings);
            action.RequestBodySource = Map(action.RequestBodySource, mappings);
            action.RequestHeadersSource = Map(action.RequestHeadersSource, mappings);
            action.TimesSource = Map(action.TimesSource, mappings);
            action.ItemsSource = Map(action.ItemsSource, mappings);
            if (action.Condition is not null)
            {
                action.Condition.LeftSource = Map(action.Condition.LeftSource, mappings);
                action.Condition.RightSource = Map(action.Condition.RightSource, mappings);
            }
        }
        foreach (var expression in imported.Locators.Locators
                     .SelectMany(locator => locator.Candidates)
                     .SelectMany(candidate => candidate.Recipe.Frames
                         .Append(candidate.Recipe.Scope)
                         .Append(candidate.Recipe.Target))
                     .Where(expression => expression?.HasText is not null))
        {
            expression!.HasText!.Source = Map(expression.HasText.Source, mappings);
        }
    }

    private static Dictionary<string, string> NormalizeMappings(
        IReadOnlyDictionary<string, string>? supplied,
        IReadOnlyList<string> required,
        string kind,
        Func<string, bool> validTarget)
    {
        supplied ??= new Dictionary<string, string>();
        var requiredSet = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpected = supplied.Keys.FirstOrDefault(key => !requiredSet.Contains(key));
        if (unexpected is not null)
        {
            throw new InvalidOperationException($"Mapping de {kind} desconhecido: {unexpected}.");
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in required)
        {
            if (!supplied.TryGetValue(source, out var target) ||
                !DataPathPattern().IsMatch(target) || !validTarget(target))
            {
                throw new InvalidOperationException(
                    $"A referência '{source}' exige mapping de {kind} para uma raiz permitida.");
            }
            result.Add(source, target);
        }
        return result;
    }

    private static void ValidateResolvedIssues(
        RecorderIssuesDocument issues,
        IReadOnlyList<string> resolvedIds)
    {
        var resolved = resolvedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocking = issues.Issues.FirstOrDefault(issue =>
            issue.Severity == RecorderIssueSeverity.Blocking && !issue.Resolved &&
            !resolved.Contains(issue.Id));
        if (blocking is not null)
        {
            throw new InvalidOperationException(
                $"A pendência bloqueante '{blocking.Id}' exige resolução explícita.");
        }
    }

    private static void RemapLocators(
        RpaPackageDocuments current,
        RpaPackageDocuments imported,
        string bundleId,
        RecorderImportApplyRequest request,
        IDictionary<string, string> remappings)
    {
        var occupied = current.Locators.Locators.Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var locator in imported.Locators.Locators)
        {
            var original = locator.Id;
            var mapped = EnsureUnique(
                original,
                occupied,
                bundleId,
                "locator",
                request.RemapConflicts,
                remappings);
            if (mapped != original)
            {
                locator.Id = mapped;
                foreach (var action in EnumerateActions(imported.Flow))
                {
                    foreach (var use in LocatorUses(action))
                    {
                        if (use.LocatorId.Equals(original, StringComparison.OrdinalIgnoreCase))
                        {
                            use.LocatorId = mapped;
                        }
                    }
                }
            }
            occupied.Add(mapped);
        }
    }

    private static void RemapCandidateIds(
        RpaPackageDocuments current,
        RpaPackageDocuments imported,
        string bundleId,
        RecorderImportApplyRequest request,
        IDictionary<string, string> remappings)
    {
        var occupied = current.Locators.Locators.SelectMany(locator => locator.Candidates)
            .Select(candidate => candidate.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in imported.Locators.Locators.SelectMany(locator => locator.Candidates))
        {
            candidate.Id = EnsureUnique(
                candidate.Id,
                occupied,
                bundleId,
                "candidate",
                request.RemapConflicts,
                remappings);
            occupied.Add(candidate.Id);
        }
    }

    private static void RemapActionIds(
        RpaPackageDocuments current,
        RpaPackageDocuments imported,
        string bundleId,
        RecorderImportApplyRequest request,
        IDictionary<string, string> remappings)
    {
        var occupied = EnumerateActions(current.Flow).Select(action => action.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var action in EnumerateActions(imported.Flow))
        {
            action.Id = EnsureUnique(
                action.Id,
                occupied,
                bundleId,
                "action",
                request.RemapConflicts,
                remappings);
            occupied.Add(action.Id);
        }
    }

    private static void RemapSubflows(
        RpaPackageDocuments current,
        RpaPackageDocuments imported,
        string bundleId,
        RecorderImportApplyRequest request,
        IDictionary<string, string> remappings)
    {
        var occupied = current.Flow.Subflows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in imported.Flow.Subflows.Keys.ToArray())
        {
            var mapped = EnsureUnique(
                name,
                occupied,
                bundleId,
                "subflow",
                request.RemapConflicts,
                remappings);
            replacements.Add(name, mapped);
            occupied.Add(mapped);
        }
        if (replacements.All(item => item.Key == item.Value)) return;
        var remapped = new Dictionary<string, List<FlowActionDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var subflow in imported.Flow.Subflows)
        {
            remapped.Add(replacements[subflow.Key], subflow.Value);
        }
        imported.Flow.Subflows = remapped;
        foreach (var action in EnumerateActions(imported.Flow))
        {
            if (action.Subflow is not null && replacements.TryGetValue(action.Subflow, out var mapped))
            {
                action.Subflow = mapped;
            }
        }
    }

    private static string EnsureUnique(
        string value,
        IEnumerable<string> occupiedValues,
        string bundleId,
        string kind,
        bool allowRemap,
        IDictionary<string, string> remappings)
    {
        var occupied = occupiedValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!occupied.Contains(value)) return value;
        if (!allowRemap)
        {
            throw new InvalidOperationException(
                $"Conflito de {kind} '{value}' exige autorização de remapeamento.");
        }
        var suffix = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{bundleId}:{kind}:{value}")))[..8].ToLowerInvariant();
        var candidate = $"{value}.recorder-{suffix}";
        var index = 2;
        while (occupied.Contains(candidate)) candidate = $"{value}.recorder-{suffix}-{index++}";
        remappings[$"{kind}:{value}"] = candidate;
        return candidate;
    }

    private static void MergeInputs(
        IList<FlowInputRequirementDefinition> target,
        IEnumerable<FlowInputRequirementDefinition> imported)
    {
        foreach (var input in imported)
        {
            var existing = target.FirstOrDefault(item =>
                item.Path.Equals(input.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                target.Add(input);
            }
            else if (!existing.Type.Equals(input.Type, StringComparison.OrdinalIgnoreCase) ||
                     existing.Required != input.Required)
            {
                throw new InvalidOperationException(
                    $"O input '{input.Path}' possui contrato incompatível no pacote aberto.");
            }
        }
    }

    private static void AddCollisions(
        string code,
        IEnumerable<string> existing,
        IEnumerable<string> incoming,
        string path,
        ICollection<RecorderImportConflict> result)
    {
        var occupied = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var value in incoming.Where(occupied.Contains).OrderBy(item => item, StringComparer.Ordinal))
        {
            result.Add(new RecorderImportConflict(
                code,
                $"{path}.{value}",
                value,
                value,
                "remapeamento determinístico mediante autorização",
                true));
        }
    }

    private static IEnumerable<string> EnumerateSources(RpaPackageDocuments package)
    {
        foreach (var action in EnumerateActions(package.Flow))
        {
            foreach (var source in new[]
                     {
                         action.ValueSource, action.NotBeforeSource,
                         action.DestinationDirectorySource, action.FileNameSource,
                         action.RequestBodySource, action.RequestHeadersSource,
                         action.TimesSource, action.ItemsSource,
                         action.Condition?.LeftSource, action.Condition?.RightSource
                     })
            {
                if (source is not null) yield return source;
            }
        }
    }

    private static IEnumerable<FlowActionDefinition> EnumerateActions(FlowDefinition flow)
    {
        foreach (var action in EnumerateActions(flow.Actions)) yield return action;
        foreach (var actions in flow.Subflows.Values)
        {
            foreach (var action in EnumerateActions(actions)) yield return action;
        }
    }

    private static IEnumerable<FlowActionDefinition> EnumerateActions(
        IEnumerable<FlowActionDefinition> actions)
    {
        foreach (var action in actions)
        {
            yield return action;
            foreach (var nested in EnumerateActions(action.Actions)) yield return nested;
            foreach (var nested in EnumerateActions(action.ElseActions)) yield return nested;
        }
    }

    private static IEnumerable<LocatorUseDefinition> LocatorUses(FlowActionDefinition action)
    {
        foreach (var use in new[]
                 {
                     action.Target, action.Trigger, action.Options, action.Ready,
                     action.Success, action.Protocol, action.Condition?.Locator
                 })
        {
            if (use is not null) yield return use;
        }
    }

    private static string? Map(string? value, IReadOnlyDictionary<string, string> mappings) =>
        value is not null && mappings.TryGetValue(value, out var mapped) ? mapped : value;

    private static RpaPackageDocuments Clone(RpaPackageDocuments source) =>
        new(
            V2JsonSerializer.Deserialize<FlowDefinition>(
                V2JsonSerializer.Serialize(source.Flow), "flow clone"),
            V2JsonSerializer.Deserialize<LocatorCatalog>(
                V2JsonSerializer.Serialize(source.Locators), "locators clone"),
            V2JsonSerializer.Deserialize<RpaPolicyDefinition>(
                V2JsonSerializer.Serialize(source.Policy), "policy clone"));

    [GeneratedRegex(
        "^(?:input|config|attachments)\\.[A-Za-z][A-Za-z0-9_-]*(?:\\.[A-Za-z][A-Za-z0-9_-]*|\\[[0-9]+\\])*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DataPathPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SubflowNamePattern();
}
