using System.Text.RegularExpressions;
using System.Text.Json;

namespace RpaFlow.Contracts.V2;

public static partial class FlowDefinitionValidator
{
    public const int MaximumNestingDepth = 32;
    public const int MaximumStructuralActions = 1_000_000;
    public const int MaximumLoopIterations = 1_000_000;

    private static readonly IReadOnlySet<string> SingularTargetTypes =
        new HashSet<string>(
        [
            "click", "clickIfVisible", "wait", "fill", "selectOption",
            "setChecked", "pressKey", "typeSequentially", "upload",
            "preserveOrFill", "select2", "fillMaskedCurrency",
            "clickAndSwitchPage", "readElement", "safeFinalConfirmation"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> ManyTargetTypes =
        new HashSet<string>(
            ["typeAcrossInputs", "readElements"],
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> WaitStates =
        new HashSet<string>(["attached", "detached", "visible", "hidden"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> OptionModes =
        new HashSet<string>(["value", "label", "index"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> PreserveComparisons =
        new HashSet<string>(["exact", "caseInsensitive", "currency"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> SelectComparisons =
        new HashSet<string>(["exact", "caseInsensitive", "numeric"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> PathOperations =
        new HashSet<string>(
            ["fileName", "fileNameWithoutExtension", "extension", "directoryName"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> ReadProperties =
        new HashSet<string>(["value", "text", "checked", "attribute"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> PageProperties =
        new HashSet<string>(["url", "title"], StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> PageComparisons =
        new HashSet<string>(["exact", "caseInsensitive", "contains"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> ConflictStrategies =
        new HashSet<string>(["unique", "fail", "overwrite"],
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> CommitKeys =
        new HashSet<string>(["Tab", "Enter"], StringComparer.OrdinalIgnoreCase);

    public static void Validate(FlowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != 2)
        {
            throw new InvalidOperationException(
                $"schemaVersion deve ser 2, mas foi {definition.SchemaVersion}.");
        }

        RequireText(definition.Name, "name");
        ValidateInputs(definition.Inputs);
        if (definition.Actions.Count == 0)
        {
            throw new InvalidOperationException("actions deve conter ao menos uma ação.");
        }

        var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actionCount = 0;
        ValidateActions(
            definition.Actions,
            "actions",
            depth: 1,
            actionIds,
            ref actionCount,
            isMainSequence: true);

        foreach (var (subflowName, actions) in definition.Subflows)
        {
            if (!SubflowNamePattern().IsMatch(subflowName))
            {
                throw new InvalidOperationException(
                    $"O nome de subfluxo '{subflowName}' é inválido.");
            }

            if (actions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"subflows.{subflowName} deve conter ao menos uma ação.");
            }

            ValidateActions(
                actions,
                $"subflows.{subflowName}",
                depth: 1,
                actionIds,
                ref actionCount,
                isMainSequence: false);
        }

        ValidateSubflowReferencesAndCycles(definition);
    }

    public static IEnumerable<(string Path, string Role, LocatorUseDefinition Use)>
        EnumerateLocatorUses(FlowDefinition definition)
    {
        foreach (var item in EnumerateLocatorUses(definition.Actions, "actions"))
        {
            yield return item;
        }

        foreach (var (name, actions) in definition.Subflows)
        {
            foreach (var item in EnumerateLocatorUses(actions, $"subflows.{name}"))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<(string Path, string Role, LocatorUseDefinition Use)>
        EnumerateLocatorUses(IReadOnlyList<FlowActionDefinition> actions, string path)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var actionPath = $"{path}[{index}]";
            foreach (var item in EnumerateActionLocatorUses(action, actionPath))
            {
                yield return item;
            }

            foreach (var item in EnumerateLocatorUses(
                         action.Actions,
                         $"{actionPath}.actions"))
            {
                yield return item;
            }

            foreach (var item in EnumerateLocatorUses(
                         action.ElseActions,
                         $"{actionPath}.elseActions"))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<(string Path, string Role, LocatorUseDefinition Use)>
        EnumerateActionLocatorUses(FlowActionDefinition action, string path)
    {
        if (action.Target is not null)
        {
            yield return ($"{path}.target", "target", action.Target);
        }

        if (action.Trigger is not null)
        {
            yield return ($"{path}.trigger", "trigger", action.Trigger);
        }

        if (action.Options is not null)
        {
            yield return ($"{path}.options", "options", action.Options);
        }

        if (action.Ready is not null)
        {
            yield return ($"{path}.ready", "ready", action.Ready);
        }

        if (action.Success is not null)
        {
            yield return ($"{path}.success", "success", action.Success);
        }

        if (action.Protocol is not null)
        {
            yield return ($"{path}.protocol", "protocol", action.Protocol);
        }

        if (action.Condition?.Locator is not null)
        {
            yield return ($"{path}.condition.locator", "condition", action.Condition.Locator);
        }
    }

    private static void ValidateInputs(IReadOnlyList<FlowInputRequirementDefinition> inputs)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            if (!DataPath.IsValid(input.Path, "input", "attachments"))
            {
                throw new InvalidOperationException(
                    $"O requisito '{input.Path}' deve usar input.<caminho> ou " +
                    "attachments.<caminho>.");
            }

            if (!paths.Add(input.Path))
            {
                throw new InvalidOperationException(
                    $"O requisito '{input.Path}' está duplicado.");
            }

            if (!AllowedInputTypes.Contains(input.Type))
            {
                throw new InvalidOperationException(
                    $"O tipo de requisito '{input.Type}' é inválido.");
            }
        }
    }

    private static void ValidateActions(
        IReadOnlyList<FlowActionDefinition> actions,
        string path,
        int depth,
        ISet<string> actionIds,
        ref int actionCount,
        bool isMainSequence)
    {
        if (depth > MaximumNestingDepth)
        {
            throw new InvalidOperationException(
                $"O fluxo excedeu {MaximumNestingDepth} níveis de aninhamento.");
        }

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var actionPath = $"{path}[{index}]";
            actionCount++;
            if (actionCount > MaximumStructuralActions)
            {
                throw new InvalidOperationException(
                    $"O fluxo excedeu {MaximumStructuralActions} ações estruturais.");
            }

            RequireText(action.Id, $"{actionPath}.id");
            RequireText(action.Type, $"{actionPath}.type");
            RequireText(action.Name, $"{actionPath}.name");
            if (!actionIds.Add(action.Id))
            {
                throw new InvalidOperationException(
                    $"O ID de ação '{action.Id}' está duplicado.");
            }

            if (!global::RpaFlow.Contracts.FlowActionCatalog.SupportedTypes.Contains(action.Type))
            {
                throw new InvalidOperationException(
                    $"{actionPath}.type '{action.Type}' não é suportado.");
            }

            ValidateCommonActionProperties(action, actionPath);
            ValidateActionSemantics(action, actionPath);

            if (action.Type.Equals(
                    "safeFinalConfirmation",
                    StringComparison.OrdinalIgnoreCase) &&
                (!isMainSequence || index != actions.Count - 1))
            {
                throw new InvalidOperationException(
                    "safeFinalConfirmation deve ser a última ação da sequência principal.");
            }

            ValidateActions(
                action.Actions,
                $"{actionPath}.actions",
                depth + 1,
                actionIds,
                ref actionCount,
                isMainSequence: false);
            ValidateActions(
                action.ElseActions,
                $"{actionPath}.elseActions",
                depth + 1,
                actionIds,
                ref actionCount,
                isMainSequence: false);
        }
    }

    private static void ValidateCommonActionProperties(
        FlowActionDefinition action,
        string path)
    {
        if (action.TimeoutMs is < 100 or > 600_000)
        {
            throw new InvalidOperationException(
                $"{path}.timeoutMs deve estar entre 100 e 600000.");
        }

        if (action.PollIntervalMs is < 50 or > 60_000)
        {
            throw new InvalidOperationException(
                $"{path}.pollIntervalMs deve estar entre 50 e 60000.");
        }

        if (action.DelayMs is < 0 or > 1_000)
        {
            throw new InvalidOperationException(
                $"{path}.delayMs deve estar entre 0 e 1000.");
        }

        if (action.Output is not null && !DataPath.IsRuntimeOutput(action.Output))
        {
            throw new InvalidOperationException(
                $"{path}.output deve usar runtime.<caminho>.");
        }

        ValidateOptionalSource(action.ValueSource, $"{path}.valueSource");
        ValidateOptionalSource(
            action.DestinationDirectorySource,
            $"{path}.destinationDirectorySource");
        ValidateOptionalSource(action.FileNameSource, $"{path}.fileNameSource");
        ValidateOptionalSource(action.RequestBodySource, $"{path}.requestBodySource");
        ValidateOptionalSource(
            action.RequestHeadersSource,
            $"{path}.requestHeadersSource");

        foreach (var (_, role, use) in EnumerateActionLocatorUses(action, path))
        {
            ValidateLocatorUse(use, $"{path}.{role}");
        }
    }

    private static void ValidateActionSemantics(
        FlowActionDefinition action,
        string path)
    {
        if (action.Target is not null &&
            !ManyTargetTypes.Contains(action.Type) &&
            action.Target.Cardinality == LocatorCardinality.Many)
        {
            throw new InvalidOperationException(
                $"{path}.target não aceita cardinalidade many.");
        }

        if (SingularTargetTypes.Contains(action.Type))
        {
            RequireLocator(action.Target, $"{path}.target");
            if (action.Target!.Cardinality == LocatorCardinality.Many)
            {
                throw new InvalidOperationException(
                    $"{path}.target não aceita cardinalidade many.");
            }
        }

        if (ManyTargetTypes.Contains(action.Type))
        {
            RequireLocator(action.Target, $"{path}.target");
            if (action.Target!.Cardinality != LocatorCardinality.Many)
            {
                throw new InvalidOperationException(
                    $"{path}.target deve usar cardinalidade many.");
            }
        }

        switch (action.Type.ToLowerInvariant())
        {
            case "navigate":
                RequireValue(action.Value, action.ValueSource, path);
                break;
            case "wait":
                RequireAllowed(action.State, WaitStates, $"{path}.state",
                    "attached, detached, visible ou hidden");
                break;
            case "fill":
            case "setchecked":
            case "presskey":
            case "typesequentially":
            case "typeacrossinputs":
            case "upload":
                RequireValue(action.Value, action.ValueSource, path);
                break;
            case "preserveorfill":
                RequireValue(action.Value, action.ValueSource, path);
                RequireAllowed(
                    action.Comparison,
                    PreserveComparisons,
                    $"{path}.comparison",
                    "exact, caseInsensitive ou currency");
                break;
            case "fillmaskedcurrency":
                RequireValue(action.Value, action.ValueSource, path);
                if (action.DecimalPlaces is < 0 or > 6)
                {
                    throw new InvalidOperationException(
                        $"{path}.decimalPlaces deve estar entre 0 e 6.");
                }

                if (action.CommitKey is not null && !CommitKeys.Contains(action.CommitKey))
                {
                    throw new InvalidOperationException(
                        $"{path}.commitKey deve ser Tab ou Enter.");
                }

                break;
            case "selectoption":
                RequireValue(action.Value, action.ValueSource, path);
                RequireAllowed(
                    action.OptionMode,
                    OptionModes,
                    $"{path}.optionMode",
                    "value, label ou index");
                break;
            case "clickandswitchpage":
                RequireLocator(action.Ready, $"{path}.ready");
                break;
            case "select2":
                RequireLocator(action.Trigger, $"{path}.trigger");
                RequireLocator(action.Options, $"{path}.options");
                if (action.Options!.Cardinality != LocatorCardinality.Many)
                {
                    throw new InvalidOperationException(
                        $"{path}.options deve usar cardinalidade many.");
                }

                RequireValue(action.Value, action.ValueSource, path);
                if (action.Comparison is not null &&
                    !SelectComparisons.Contains(action.Comparison))
                {
                    throw new InvalidOperationException(
                        $"{path}.comparison deve ser exact, caseInsensitive ou numeric.");
                }

                break;
            case "screenshot":
                ValidateArtifactDestination(action, path);
                break;
            case "safefinalconfirmation":
                ValidateArtifactDestination(action, path);
                ValidateFinalConfirmationResult(action, path);
                break;
            case "fail":
                RequireValue(action.Value, action.ValueSource, path);
                break;
            case "transformpath":
                RequireValue(action.Value, action.ValueSource, path);
                RequireText(action.Output, $"{path}.output");
                RequireAllowed(
                    action.Operation,
                    PathOperations,
                    $"{path}.operation",
                    "fileName, fileNameWithoutExtension, extension ou directoryName");
                break;
            case "waitforonetimecode":
                RequireText(action.ProviderAlias, $"{path}.providerAlias");
                if (!IdentifierPattern().IsMatch(action.ProviderAlias!))
                {
                    throw new InvalidOperationException(
                        $"{path}.providerAlias possui formato inválido.");
                }

                RequireText(action.NotBeforeSource, $"{path}.notBeforeSource");
                ValidateOptionalSource(action.NotBeforeSource, $"{path}.notBeforeSource");
                RequireText(action.Output, $"{path}.output");
                if (action.TimeoutMs is null or < 1_000 or > 600_000)
                {
                    throw new InvalidOperationException(
                        $"{path}.timeoutMs deve estar entre 1000 e 600000.");
                }

                if (action.PollIntervalMs is null or < 500 or > 60_000)
                {
                    throw new InvalidOperationException(
                        $"{path}.pollIntervalMs deve estar entre 500 e 60000.");
                }

                if (action.PollIntervalMs > action.TimeoutMs)
                {
                    throw new InvalidOperationException(
                        $"{path}.pollIntervalMs não pode ser maior que timeoutMs.");
                }

                break;
            case "setvariable":
                RequireValue(action.Value, action.ValueSource, path);
                RequireText(action.Output, $"{path}.output");
                break;
            case "download" when string.Equals(
                action.DownloadMode,
                "click",
                StringComparison.OrdinalIgnoreCase):
                RequireLocator(action.Target, $"{path}.target");
                ValidateArtifactDestination(action, path);
                break;
            case "download" when string.Equals(
                action.DownloadMode,
                "request",
                StringComparison.OrdinalIgnoreCase):
                RequireValue(action.Value, action.ValueSource, path);
                if (action.Method?.ToUpperInvariant() is not ("GET" or "POST"))
                {
                    throw new InvalidOperationException(
                        $"{path}.method deve ser GET ou POST.");
                }

                RequireOptionalExactlyOne(
                    action.RequestBody,
                    action.RequestBodySource,
                    $"{path}.requestBody/requestBodySource");
                RequireOptionalExactlyOne(
                    action.RequestHeaders,
                    action.RequestHeadersSource,
                    $"{path}.requestHeaders/requestHeadersSource");
                if (action.BodyType is not null &&
                    action.BodyType.ToLowerInvariant() is not ("json" or "text" or "form"))
                {
                    throw new InvalidOperationException(
                        $"{path}.bodyType deve ser json, text ou form.");
                }

                if (HasLiteral(action.RequestHeaders) &&
                    action.RequestHeaders.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(
                        $"{path}.requestHeaders deve ser um objeto JSON.");
                }

                if (action.BodyType?.Equals("form", StringComparison.OrdinalIgnoreCase) == true &&
                    HasLiteral(action.RequestBody) &&
                    action.RequestBody.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(
                        $"{path}.requestBody deve ser objeto JSON para bodyType form.");
                }

                ValidateArtifactDestination(action, path);
                break;
            case "download":
                throw new InvalidOperationException(
                    $"{path}.downloadMode deve ser click ou request.");
            case "readelement":
            case "readelements":
                RequireText(action.Output, $"{path}.output");
                RequireAllowed(
                    action.Property,
                    ReadProperties,
                    $"{path}.property",
                    "value, text, checked ou attribute");
                if (string.Equals(
                        action.Property,
                        "attribute",
                        StringComparison.OrdinalIgnoreCase))
                {
                    RequireText(action.Attribute, $"{path}.attribute");
                }

                if (action.Type.Equals("readElements", StringComparison.OrdinalIgnoreCase) &&
                    action.MaxItems is < 1 or > 10_000)
                {
                    throw new InvalidOperationException(
                        $"{path}.maxItems deve estar entre 1 e 10000.");
                }

                break;
            case "switchpage":
                RequireValue(action.Value, action.ValueSource, path);
                RequireAllowed(
                    action.Property,
                    PageProperties,
                    $"{path}.property",
                    "url ou title");
                RequireAllowed(
                    action.Comparison,
                    PageComparisons,
                    $"{path}.comparison",
                    "exact, caseInsensitive ou contains");
                break;
            case "capturetimestamp":
                RequireText(action.Output, $"{path}.output");
                break;
            case "if":
                ValidateCondition(action.Condition, $"{path}.condition");
                if (action.Actions.Count == 0 && action.ElseActions.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"{path} deve possuir actions ou elseActions.");
                }

                break;
            case "repeat":
                RequireExactlyOne(
                    action.Times.HasValue,
                    !string.IsNullOrWhiteSpace(action.TimesSource),
                    $"{path}.times/timesSource");
                if (action.Times is < 0 or > 1_000_000)
                {
                    throw new InvalidOperationException(
                        $"{path}.times deve estar entre 0 e 1000000.");
                }

                RequireNestedActions(action.Actions, path);

                break;
            case "foreach":
                RequireExactlyOne(
                    action.Items is not null,
                    !string.IsNullOrWhiteSpace(action.ItemsSource),
                    $"{path}.items/itemsSource");
                RequireVariableName(action.ItemVariable, $"{path}.itemVariable");
                if (action.IndexVariable is not null)
                {
                    RequireVariableName(action.IndexVariable, $"{path}.indexVariable");
                }

                if (action.Items?.Count > MaximumLoopIterations)
                {
                    throw new InvalidOperationException(
                        $"{path}.items ultrapassa {MaximumLoopIterations} itens.");
                }

                RequireNestedActions(action.Actions, path);
                break;
            case "runsubflow":
                RequireText(action.Subflow, $"{path}.subflow");
                break;
        }
    }

    private static void ValidateCondition(FlowConditionDefinition? condition, string path)
    {
        if (condition is null)
        {
            throw new InvalidOperationException($"{path} é obrigatório.");
        }

        switch (condition.Type.ToLowerInvariant())
        {
            case "element":
                RequireLocator(condition.Locator, $"{path}.locator");
                RequireAllowed(
                    condition.State,
                    WaitStates,
                    $"{path}.state",
                    "attached, detached, visible ou hidden");
                break;
            case "value":
                RequireText(condition.Operator, $"{path}.operator");
                RequireValue(condition.LeftValue, condition.LeftSource, $"{path}.left");
                RequireValue(condition.RightValue, condition.RightSource, $"{path}.right");
                ValidateOptionalSource(condition.LeftSource, $"{path}.leftSource");
                ValidateOptionalSource(condition.RightSource, $"{path}.rightSource");
                break;
            default:
                throw new InvalidOperationException(
                    $"{path}.type deve ser value ou element.");
        }
    }

    private static void ValidateLocatorUse(LocatorUseDefinition use, string path)
    {
        RequireText(use.LocatorId, $"{path}.locatorId");
    }

    private static void ValidateSubflowReferencesAndCycles(FlowDefinition definition)
    {
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in definition.Subflows.Keys)
        {
            calls[name] = CollectSubflowCalls(definition.Subflows[name]);
            foreach (var target in calls[name])
            {
                if (!definition.Subflows.ContainsKey(target))
                {
                    throw new InvalidOperationException(
                        $"O subfluxo '{name}' referencia '{target}', que não existe.");
                }
            }
        }

        foreach (var target in CollectSubflowCalls(definition.Actions))
        {
            if (!definition.Subflows.ContainsKey(target))
            {
                throw new InvalidOperationException(
                    $"A sequência principal referencia o subfluxo '{target}', que não existe.");
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in calls.Keys)
        {
            Visit(name, calls, visited, active);
        }
    }

    private static HashSet<string> CollectSubflowCalls(
        IReadOnlyList<FlowActionDefinition> actions)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in actions)
        {
            if (action.Type.Equals("runSubflow", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(action.Subflow))
            {
                result.Add(action.Subflow);
            }

            result.UnionWith(CollectSubflowCalls(action.Actions));
            result.UnionWith(CollectSubflowCalls(action.ElseActions));
        }

        return result;
    }

    private static void Visit(
        string name,
        IReadOnlyDictionary<string, HashSet<string>> calls,
        ISet<string> visited,
        ISet<string> active)
    {
        if (visited.Contains(name))
        {
            return;
        }

        if (!active.Add(name))
        {
            throw new InvalidOperationException(
                $"Foi detectado ciclo envolvendo o subfluxo '{name}'.");
        }

        foreach (var target in calls[name])
        {
            Visit(target, calls, visited, active);
        }

        active.Remove(name);
        visited.Add(name);
    }

    private static void RequireLocator(LocatorUseDefinition? use, string path)
    {
        if (use is null)
        {
            throw new InvalidOperationException($"{path} é obrigatório.");
        }

        ValidateLocatorUse(use, path);
    }

    private static void RequireVariableName(string? value, string path)
    {
        RequireText(value, path);
        if (!VariableNamePattern().IsMatch(value!))
        {
            throw new InvalidOperationException($"{path} possui nome inválido.");
        }
    }

    private static void RequireValue(JsonElement literal, string? source, string path)
    {
        if (HasLiteral(literal) == !string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"{path} deve informar exatamente um valor literal ou source.");
        }

        ValidateOptionalSource(source, $"{path}.source");
    }

    private static bool HasLiteral(JsonElement value) =>
        value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private static void RequireOptionalExactlyOne(
        JsonElement literal,
        string? source,
        string path)
    {
        if (HasLiteral(literal) && !string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"{path} não aceita literal e source simultaneamente.");
        }

        ValidateOptionalSource(source, path);
    }

    private static void RequireOptionalExactlyOne(
        string? literal,
        string? source,
        string path)
    {
        if (!string.IsNullOrWhiteSpace(literal) && !string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"{path} não aceita literal e source simultaneamente.");
        }

        ValidateOptionalSource(source, path);
    }

    private static void ValidateArtifactDestination(
        FlowActionDefinition action,
        string path)
    {
        RequireOptionalExactlyOne(
            action.DestinationDirectory,
            action.DestinationDirectorySource,
            $"{path}.destinationDirectory/destinationDirectorySource");
        RequireOptionalExactlyOne(
            action.FileName,
            action.FileNameSource,
            $"{path}.fileName/fileNameSource");
        if (action.ConflictStrategy is not null &&
            !ConflictStrategies.Contains(action.ConflictStrategy))
        {
            throw new InvalidOperationException(
                $"{path}.conflictStrategy deve ser unique, fail ou overwrite.");
        }
    }

    private static void ValidateFinalConfirmationResult(
        FlowActionDefinition action,
        string path)
    {
        var fields = new (string Name, string? Value)[]
        {
            ("successText", action.SuccessText),
            ("protocolPattern", action.ProtocolPattern),
            ("completionTarget", action.CompletionTarget),
            ("confirmationMessageTarget", action.ConfirmationMessageTarget),
            ("protocolTarget", action.ProtocolTarget)
        };
        var hasLocatorResult = action.Success is not null || action.Protocol is not null;
        if (!hasLocatorResult && fields.All(field => field.Value is null))
        {
            return;
        }

        RequireLocator(action.Success, $"{path}.success");
        RequireLocator(action.Protocol, $"{path}.protocol");
        foreach (var field in fields)
        {
            RequireText(field.Value, $"{path}.{field.Name}");
        }

        foreach (var target in new[]
                 {
                     action.CompletionTarget,
                     action.ConfirmationMessageTarget,
                     action.ProtocolTarget
                 })
        {
            if (!DataPath.IsRuntimeOutput(target!))
            {
                throw new InvalidOperationException(
                    $"{path} possui destino final fora de runtime.*.");
            }
        }

        if (new[]
            {
                action.CompletionTarget!,
                action.ConfirmationMessageTarget!,
                action.ProtocolTarget!
            }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
        {
            throw new InvalidOperationException(
                $"{path} exige três destinos finais diferentes.");
        }

        try
        {
            var expression = new Regex(
                action.ProtocolPattern!,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            if (!expression.GetGroupNames().Contains(
                    "protocol",
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{path}.protocolPattern exige o grupo nomeado protocol.");
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{path}.protocolPattern é inválido.",
                exception);
        }
    }

    private static void RequireNestedActions(
        IReadOnlyCollection<FlowActionDefinition> actions,
        string path)
    {
        if (actions.Count == 0)
        {
            throw new InvalidOperationException($"{path}.actions é obrigatório.");
        }
    }

    private static void RequireAllowed(
        string? value,
        IReadOnlySet<string> allowed,
        string path,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value) || !allowed.Contains(value))
        {
            throw new InvalidOperationException($"{path} deve ser {description}.");
        }
    }

    private static void RequireExactlyOne(bool first, bool second, string path)
    {
        if (first == second)
        {
            throw new InvalidOperationException(
                $"{path} deve informar exatamente uma das alternativas.");
        }
    }

    private static void ValidateOptionalSource(string? source, string path)
    {
        if (source is not null && !DataPath.IsReadable(source))
        {
            throw new InvalidOperationException(
                $"{path} deve usar uma raiz de dados reconhecida.");
        }
    }

    private static void RequireText(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{path} é obrigatório.");
        }
    }

    private static readonly IReadOnlySet<string> AllowedInputTypes =
        new HashSet<string>(
            ["any", "string", "number", "boolean", "object", "array", "null"],
            StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SubflowNamePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VariableNamePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}

internal static partial class DataPath
{
    private static readonly string[] ReadableRoots =
        ["input", "config", "attachments", "secret", "runtime", "system", "loop"];

    public static bool IsReadable(string path) => IsValid(path, ReadableRoots);

    public static bool IsRuntimeOutput(string path) => IsValid(path, "runtime");

    public static bool IsValid(string path, params string[] roots)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == path.Length - 1)
        {
            return false;
        }

        var root = path[..dot];
        return roots.Contains(root, StringComparer.OrdinalIgnoreCase) &&
            PathPattern().IsMatch(path);
    }

    [GeneratedRegex(
        "^[A-Za-z][A-Za-z0-9_-]*(?:\\.[A-Za-z][A-Za-z0-9_-]*|\\[[0-9]+\\])+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
