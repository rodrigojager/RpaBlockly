using System.Text.Json;
using System.Text.RegularExpressions;

namespace RpaFlow.Contracts;

public static class FlowDefinitionValidator
{
    public const int MaximumNestingDepth = 32;
    public const int MaximumFrameDepth = 8;
    public const int MaximumStructuralActions = 1_000_000;
    public const int MaximumLoopIterations = 1_000_000;

    private static readonly HashSet<string> WaitStates = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "attached", "detached", "visible", "hidden"
    };

    private static readonly HashSet<string> Comparisons = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "exact", "caseInsensitive", "currency"
    };

    private static readonly HashSet<string> MatchModes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "first", "single"
    };

    private static readonly HashSet<string> SelectComparisons = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "exact", "caseInsensitive", "numeric"
    };

    private static readonly HashSet<string> CommitKeys = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Tab", "Enter"
    };

    private static readonly HashSet<string> OptionModes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "value", "label", "index"
    };

    private static readonly HashSet<string> PageProperties = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "url", "title"
    };

    private static readonly HashSet<string> PageComparisons = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "exact", "caseInsensitive", "contains"
    };

    private static readonly HashSet<string> PathOperations = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "fileName", "fileNameWithoutExtension", "extension", "directoryName"
    };

    private static readonly HashSet<string> ConditionOperators = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "equals",
        "notEquals",
        "contains",
        "notContains",
        "startsWith",
        "endsWith",
        "matchesRegex",
        "isEmpty",
        "isNotEmpty"
    };

    private static readonly HashSet<string> ConflictStrategies = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "unique", "fail", "overwrite"
    };

    public static void Validate(FlowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();

        if (definition.SchemaVersion != 1)
        {
            errors.Add(
                $"schemaVersion deve ser 1; recebido {definition.SchemaVersion}.");
        }

        Require(definition.Name, "name", errors);
        definition.Inputs ??= [];
        ValidateInputDefinitions(definition.Inputs, errors);
        if (definition.Actions is null || definition.Actions.Count == 0)
        {
            errors.Add("actions deve possuir pelo menos uma ação.");
        }

        definition.Subflows ??=
            new Dictionary<string, List<FlowActionDefinition>>(
                StringComparer.OrdinalIgnoreCase);
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subflowNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var structuralCount = 0;
        var safeFinalLocations = new List<(string Path, bool IsAllowed)>();

        ValidateActionList(
            definition.Actions ?? [],
            "actions",
            isMainSequence: true,
            depth: 0,
            identifiers,
            safeFinalLocations,
            ref structuralCount,
            errors);

        foreach (var (name, actions) in definition.Subflows)
        {
            if (!IsIdentifier(name))
            {
                errors.Add(
                    $"Nome de subfluxo inválido: '{name}'. Use letras, números, ponto, hífen ou sublinhado.");
            }

            if (!subflowNames.Add(name))
            {
                errors.Add($"Nome de subfluxo duplicado: '{name}'.");
            }

            if (actions is null || actions.Count == 0)
            {
                errors.Add($"subflows.{name} deve possuir pelo menos uma ação.");
                continue;
            }

            ValidateActionList(
                actions,
                $"subflows.{name}",
                isMainSequence: false,
                depth: 0,
                identifiers,
                safeFinalLocations,
                ref structuralCount,
                errors);
        }

        if (safeFinalLocations.Count > 1)
        {
            errors.Add("O fluxo pode possuir no máximo uma confirmação final segura.");
        }
        else if (safeFinalLocations.Count == 1 && !safeFinalLocations[0].IsAllowed)
        {
            errors.Add(
                "safeFinalConfirmation deve ser a última ação da sequência principal e não pode ficar em condição, repetição ou subfluxo.");
        }

        ValidateSubflowReferencesAndCycles(definition, errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Fluxo de produção inválido:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }
    }

    private static void ValidateActionList(
        IReadOnlyList<FlowActionDefinition> actions,
        string path,
        bool isMainSequence,
        int depth,
        ISet<string> identifiers,
        ICollection<(string Path, bool IsAllowed)> safeFinalLocations,
        ref int structuralCount,
        ICollection<string> errors)
    {
        if (depth > MaximumNestingDepth)
        {
            errors.Add($"{path} ultrapassa {MaximumNestingDepth} níveis de aninhamento.");
            return;
        }

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var prefix = $"{path}[{index}]";
            structuralCount++;
            if (structuralCount > MaximumStructuralActions)
            {
                errors.Add(
                    $"O fluxo ultrapassa o limite estrutural de {MaximumStructuralActions} ações.");
                return;
            }

            if (action is null)
            {
                errors.Add($"{prefix} deve ser um objeto.");
                continue;
            }

            Require(action.Id, $"{prefix}.id", errors);
            Require(action.Type, $"{prefix}.type", errors);
            Require(action.Name, $"{prefix}.name", errors);

            if (string.IsNullOrWhiteSpace(action.Type))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(action.Id) && !IsIdentifier(action.Id))
            {
                errors.Add(
                    $"{prefix}.id é inválido: '{action.Id}'. " +
                    "Use letras, números, ponto, hífen ou sublinhado, começando por letra.");
            }

            if (!string.IsNullOrWhiteSpace(action.Id) && !identifiers.Add(action.Id))
            {
                errors.Add($"{prefix}.id está duplicado: '{action.Id}'.");
            }

            if (!FlowActionCatalog.SupportedTypes.Contains(action.Type))
            {
                errors.Add($"{prefix}.type não é suportado: '{action.Type}'.");
                continue;
            }

            if (action.Type.Equals(
                "safeFinalConfirmation",
                StringComparison.OrdinalIgnoreCase))
            {
                safeFinalLocations.Add((prefix, isMainSequence && index == actions.Count - 1));
            }

            ValidateAction(
                action,
                prefix,
                depth,
                identifiers,
                safeFinalLocations,
                ref structuralCount,
                errors);
        }
    }

    private static void ValidateAction(
        FlowActionDefinition action,
        string prefix,
        int depth,
        ISet<string> identifiers,
        ICollection<(string Path, bool IsAllowed)> safeFinalLocations,
        ref int structuralCount,
        ICollection<string> errors)
    {
        action.Actions ??= [];
        action.ElseActions ??= [];

        switch (action.Type.ToLowerInvariant())
        {
            case "navigate":
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                break;
            case "click":
            case "clickifvisible":
            case "wait":
                Require(action.Selector, $"{prefix}.selector", errors);
                break;
            case "safefinalconfirmation":
                Require(action.Selector, $"{prefix}.selector", errors);
                ValidateArtifactDestination(action, prefix, errors);
                ValidateFinalConfirmationResult(action, prefix, errors);
                break;
            case "fill":
            case "setchecked":
            case "presskey":
            case "typesequentially":
            case "typeacrossinputs":
            case "preserveorfill":
            case "fillmaskedcurrency":
            case "upload":
                Require(action.Selector, $"{prefix}.selector", errors);
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                break;
            case "selectoption":
                Require(action.Selector, $"{prefix}.selector", errors);
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                if (!OptionModes.Contains(action.OptionMode ?? string.Empty))
                {
                    errors.Add($"{prefix}.optionMode deve ser value, label ou index.");
                }
                break;
            case "clickandswitchpage":
                Require(action.Selector, $"{prefix}.selector", errors);
                Require(action.ReadySelector, $"{prefix}.readySelector", errors);
                break;
            case "select2":
                Require(action.Selector, $"{prefix}.selector", errors);
                Require(action.TriggerSelector, $"{prefix}.triggerSelector", errors);
                Require(action.OptionSelector, $"{prefix}.optionSelector", errors);
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                break;
            case "screenshot":
                ValidateArtifactDestination(action, prefix, errors);
                break;
            case "fail":
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                break;
            case "transformpath":
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                ValidateRuntimeTarget(action.Target, $"{prefix}.target", errors);
                if (!PathOperations.Contains(action.Operation ?? string.Empty))
                {
                    errors.Add(
                        $"{prefix}.operation deve ser fileName, fileNameWithoutExtension, extension ou directoryName.");
                }
                break;
            case "capturetimestamp":
                ValidateRuntimeTarget(action.Target, $"{prefix}.target", errors);
                break;
            case "waitforonetimecode":
                Require(action.ProviderAlias, $"{prefix}.providerAlias", errors);
                if (!string.IsNullOrWhiteSpace(action.ProviderAlias) &&
                    !IsIdentifier(action.ProviderAlias))
                {
                    errors.Add(
                        $"{prefix}.providerAlias possui formato inválido. " +
                        "Use letras, números, ponto, hífen ou sublinhado, começando por letra.");
                }
                Require(action.NotBeforeSource, $"{prefix}.notBeforeSource", errors);
                ValidateValueSource(
                    action.NotBeforeSource,
                    $"{prefix}.notBeforeSource",
                    errors);
                ValidateRuntimeTarget(action.Target, $"{prefix}.target", errors);
                if (action.TimeoutMs is null)
                {
                    errors.Add($"{prefix}.timeoutMs é obrigatório.");
                }
                else if (action.TimeoutMs is < 1_000 or > 600_000)
                {
                    errors.Add(
                        $"{prefix}.timeoutMs deve estar entre 1000 e 600000.");
                }

                if (action.PollIntervalMs is null)
                {
                    errors.Add($"{prefix}.pollIntervalMs é obrigatório.");
                }
                else if (action.PollIntervalMs is < 500 or > 60_000)
                {
                    errors.Add(
                        $"{prefix}.pollIntervalMs deve estar entre 500 e 60000.");
                }

                if (action.TimeoutMs is not null &&
                    action.PollIntervalMs is not null &&
                    action.PollIntervalMs > action.TimeoutMs)
                {
                    errors.Add(
                        $"{prefix}.pollIntervalMs não pode ser maior que timeoutMs.");
                }
                break;
            case "setvariable":
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                ValidateRuntimeTarget(action.Target, $"{prefix}.target", errors);
                break;
            case "readelement":
            case "readelements":
                Require(action.Selector, $"{prefix}.selector", errors);
                ValidateRuntimeTarget(action.Target, $"{prefix}.target", errors);
                if (action.Property?.ToLowerInvariant() is not
                    ("value" or "text" or "checked" or "attribute"))
                {
                    errors.Add(
                        $"{prefix}.property deve ser value, text, checked ou attribute.");
                }
                else if (action.Property.Equals("attribute", StringComparison.OrdinalIgnoreCase))
                {
                    Require(action.Attribute, $"{prefix}.attribute", errors);
                }
                if (action.Type.Equals("readElements", StringComparison.OrdinalIgnoreCase) &&
                    action.MaxItems is < 1 or > 10_000)
                {
                    errors.Add($"{prefix}.maxItems deve estar entre 1 e 10000.");
                }
                break;
            case "switchpage":
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                if (!PageProperties.Contains(action.Property ?? string.Empty))
                {
                    errors.Add($"{prefix}.property deve ser url ou title.");
                }
                if (!PageComparisons.Contains(action.Comparison ?? string.Empty))
                {
                    errors.Add(
                        $"{prefix}.comparison deve ser exact, caseInsensitive ou contains.");
                }
                break;
            case "closepage":
            case "completeauthenticationattempt":
                break;
            case "download":
                ValidateDownload(action, prefix, errors);
                ValidateArtifactDestination(action, prefix, errors);
                break;
            case "if":
                ValidateCondition(action.Condition, $"{prefix}.condition", errors);
                if (action.Actions.Count == 0 && action.ElseActions.Count == 0)
                {
                    errors.Add($"{prefix} deve possuir actions ou elseActions.");
                }

                ValidateActionList(
                    action.Actions,
                    $"{prefix}.actions",
                    isMainSequence: false,
                    depth + 1,
                    identifiers,
                    safeFinalLocations,
                    ref structuralCount,
                    errors);
                ValidateActionList(
                    action.ElseActions,
                    $"{prefix}.elseActions",
                    isMainSequence: false,
                    depth + 1,
                    identifiers,
                    safeFinalLocations,
                    ref structuralCount,
                    errors);
                break;
            case "repeat":
                RequireExclusive(
                    action.Times?.ToString(),
                    action.TimesSource,
                    $"{prefix}.times",
                    $"{prefix}.timesSource",
                    errors);
                if (action.Times is < 0 or > MaximumLoopIterations)
                {
                    errors.Add(
                        $"{prefix}.times deve estar entre 0 e {MaximumLoopIterations}.");
                }

                ValidateValueSource(action.TimesSource, $"{prefix}.timesSource", errors);
                RequireNestedActions(action.Actions, prefix, errors);
                ValidateActionList(
                    action.Actions,
                    $"{prefix}.actions",
                    isMainSequence: false,
                    depth + 1,
                    identifiers,
                    safeFinalLocations,
                    ref structuralCount,
                    errors);
                break;
            case "foreach":
                if ((action.Items is null) == string.IsNullOrWhiteSpace(action.ItemsSource))
                {
                    errors.Add($"{prefix} deve usar somente items ou itemsSource.");
                }

                if (action.Items?.Count > MaximumLoopIterations)
                {
                    errors.Add(
                        $"{prefix}.items ultrapassa {MaximumLoopIterations} itens.");
                }

                ValidateValueSource(action.ItemsSource, $"{prefix}.itemsSource", errors);
                if (!IsVariableName(action.ItemVariable))
                {
                    errors.Add($"{prefix}.itemVariable possui nome inválido.");
                }
                if (!string.IsNullOrWhiteSpace(action.IndexVariable) &&
                    !IsVariableName(action.IndexVariable))
                {
                    errors.Add($"{prefix}.indexVariable possui nome inválido.");
                }

                RequireNestedActions(action.Actions, prefix, errors);
                ValidateActionList(
                    action.Actions,
                    $"{prefix}.actions",
                    isMainSequence: false,
                    depth + 1,
                    identifiers,
                    safeFinalLocations,
                    ref structuralCount,
                    errors);
                break;
            case "runsubflow":
                Require(action.Subflow, $"{prefix}.subflow", errors);
                break;
        }

        if (action.Type.Equals("wait", StringComparison.OrdinalIgnoreCase) &&
            !WaitStates.Contains(action.State ?? string.Empty))
        {
            errors.Add(
                $"{prefix}.state deve ser attached, detached, visible ou hidden.");
        }

        if (action.Type.Equals("preserveOrFill", StringComparison.OrdinalIgnoreCase) &&
            !Comparisons.Contains(action.Comparison ?? string.Empty))
        {
            errors.Add(
                $"{prefix}.comparison deve ser exact, caseInsensitive ou currency.");
        }

        if (action.Type.Equals("select2", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(action.Comparison) &&
            !SelectComparisons.Contains(action.Comparison))
        {
            errors.Add(
                $"{prefix}.comparison deve ser exact, caseInsensitive ou numeric.");
        }

        if (action.Type.Equals("wait", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(action.MatchMode) &&
            !MatchModes.Contains(action.MatchMode))
        {
            errors.Add($"{prefix}.matchMode deve ser first ou single.");
        }

        if (action.Type.Equals(
                "typeAcrossInputs",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(action.MatchMode))
        {
            errors.Add($"{prefix}.matchMode não é aceito por typeAcrossInputs.");
        }

        if (action.Type.Equals("fillMaskedCurrency", StringComparison.OrdinalIgnoreCase))
        {
            if (action.DecimalPlaces is < 0 or > 6)
            {
                errors.Add($"{prefix}.decimalPlaces deve estar entre 0 e 6.");
            }

            if (!string.IsNullOrWhiteSpace(action.CommitKey) &&
                !CommitKeys.Contains(action.CommitKey))
            {
                errors.Add($"{prefix}.commitKey deve ser Tab ou Enter.");
            }
        }

        if (!action.Type.Equals(
                "waitForOneTimeCode",
                StringComparison.OrdinalIgnoreCase) &&
            action.TimeoutMs is < 100 or > 600_000)
        {
            errors.Add($"{prefix}.timeoutMs deve estar entre 100 e 600000.");
        }

        if (action.DelayMs is < 0 or > 1_000)
        {
            errors.Add($"{prefix}.delayMs deve estar entre 0 e 1000.");
        }

        action.FrameSelectors ??= [];
        ValidateFrameSelectors(action.FrameSelectors, $"{prefix}.frameSelectors", errors);
        ValidateLocatorText(
            action.Scope,
            action.ScopeHasText,
            action.ScopeHasTextSource,
            action.HasText,
            action.HasTextSource,
            prefix,
            errors);
        ValidateValueSource(action.ValueSource, $"{prefix}.valueSource", errors);
    }

    private static void ValidateDownload(
        FlowActionDefinition action,
        string prefix,
        ICollection<string> errors)
    {
        switch (action.DownloadMode?.ToLowerInvariant())
        {
            case "click":
                Require(action.Selector, $"{prefix}.selector", errors);
                break;
            case "request":
                RequireValue(action.Value, action.ValueSource, prefix, errors);
                if (action.Method?.ToUpperInvariant() is not ("GET" or "POST"))
                {
                    errors.Add($"{prefix}.method deve ser GET ou POST.");
                }

                RequireOptionalExclusive(
                    action.RequestBody,
                    action.RequestBodySource,
                    $"{prefix}.requestBody",
                    $"{prefix}.requestBodySource",
                    errors);
                RequireOptionalExclusive(
                    action.RequestHeaders,
                    action.RequestHeadersSource,
                    $"{prefix}.requestHeaders",
                    $"{prefix}.requestHeadersSource",
                    errors);
                ValidateValueSource(
                    action.RequestBodySource,
                    $"{prefix}.requestBodySource",
                    errors);
                ValidateValueSource(
                    action.RequestHeadersSource,
                    $"{prefix}.requestHeadersSource",
                    errors);
                if (!string.IsNullOrWhiteSpace(action.BodyType) &&
                    action.BodyType.ToLowerInvariant() is not ("json" or "text" or "form"))
                {
                    errors.Add($"{prefix}.bodyType deve ser json, text ou form.");
                }
                if (HasLiteral(action.RequestHeaders) &&
                    action.RequestHeaders.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    errors.Add($"{prefix}.requestHeaders deve ser um objeto JSON.");
                }
                if (action.BodyType?.Equals(
                        "form",
                        StringComparison.OrdinalIgnoreCase) == true &&
                    HasLiteral(action.RequestBody) &&
                    action.RequestBody.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    errors.Add($"{prefix}.requestBody deve ser um objeto JSON para bodyType form.");
                }
                break;
            default:
                errors.Add($"{prefix}.downloadMode deve ser click ou request.");
                break;
        }

    }

    private static void ValidateArtifactDestination(
        FlowActionDefinition action,
        string prefix,
        ICollection<string> errors)
    {
        RequireOptionalExclusive(
            action.DestinationDirectory,
            action.DestinationDirectorySource,
            $"{prefix}.destinationDirectory",
            $"{prefix}.destinationDirectorySource",
            errors);
        RequireOptionalExclusive(
            action.FileName,
            action.FileNameSource,
            $"{prefix}.fileName",
            $"{prefix}.fileNameSource",
            errors);
        ValidateValueSource(
            action.DestinationDirectorySource,
            $"{prefix}.destinationDirectorySource",
            errors);
        ValidateValueSource(
            action.FileNameSource,
            $"{prefix}.fileNameSource",
            errors);
        if (!string.IsNullOrWhiteSpace(action.ConflictStrategy) &&
            !ConflictStrategies.Contains(action.ConflictStrategy))
        {
            errors.Add($"{prefix}.conflictStrategy deve ser unique, fail ou overwrite.");
        }
        if (!string.IsNullOrWhiteSpace(action.Target))
        {
            ValidateRuntimeTarget(action.Target, $"{prefix}.target", errors);
        }
    }

    private static void ValidateFinalConfirmationResult(
        FlowActionDefinition action,
        string prefix,
        ICollection<string> errors)
    {
        var properties = new (string Name, string? Value)[]
        {
            ("successSelector", action.SuccessSelector),
            ("successText", action.SuccessText),
            ("protocolSelector", action.ProtocolSelector),
            ("protocolPattern", action.ProtocolPattern),
            ("completionTarget", action.CompletionTarget),
            ("confirmationMessageTarget", action.ConfirmationMessageTarget),
            ("protocolTarget", action.ProtocolTarget)
        };
        if (properties.All(property => string.IsNullOrWhiteSpace(property.Value)))
        {
            return;
        }

        foreach (var property in properties)
        {
            Require(property.Value, $"{prefix}.{property.Name}", errors);
        }

        ValidateRuntimeTarget(
            action.CompletionTarget,
            $"{prefix}.completionTarget",
            errors);
        ValidateRuntimeTarget(
            action.ConfirmationMessageTarget,
            $"{prefix}.confirmationMessageTarget",
            errors);
        ValidateRuntimeTarget(
            action.ProtocolTarget,
            $"{prefix}.protocolTarget",
            errors);

        var targets = new[]
        {
            action.CompletionTarget,
            action.ConfirmationMessageTarget,
            action.ProtocolTarget
        };
        if (targets.All(target => !string.IsNullOrWhiteSpace(target)) &&
            targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
        {
            errors.Add(
                $"{prefix}: completionTarget, confirmationMessageTarget e protocolTarget " +
                "devem ser destinos diferentes.");
        }

        if (string.IsNullOrWhiteSpace(action.ProtocolPattern))
        {
            return;
        }

        try
        {
            var expression = new Regex(
                action.ProtocolPattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            if (!expression.GetGroupNames().Contains(
                    "protocol",
                    StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"{prefix}.protocolPattern deve possuir o grupo nomeado 'protocol'.");
            }
        }
        catch (ArgumentException exception)
        {
            errors.Add($"{prefix}.protocolPattern é inválido: {exception.Message}");
        }
    }

    private static void ValidateCondition(
        FlowConditionDefinition? condition,
        string prefix,
        ICollection<string> errors)
    {
        if (condition is null)
        {
            errors.Add($"{prefix} é obrigatório.");
            return;
        }

        if (string.Equals(
            condition.Type,
            "element",
            StringComparison.OrdinalIgnoreCase))
        {
            Require(condition.Selector, $"{prefix}.selector", errors);
            condition.FrameSelectors ??= [];
            ValidateFrameSelectors(
                condition.FrameSelectors,
                $"{prefix}.frameSelectors",
                errors);
            ValidateLocatorText(
                condition.Scope,
                condition.ScopeHasText,
                condition.ScopeHasTextSource,
                condition.HasText,
                condition.HasTextSource,
                prefix,
                errors);
            if (!WaitStates.Contains(condition.State ?? string.Empty))
            {
                errors.Add(
                    $"{prefix}.state deve ser attached, detached, visible ou hidden.");
            }


            if (!string.IsNullOrWhiteSpace(condition.MatchMode) &&
                !MatchModes.Contains(condition.MatchMode))
            {
                errors.Add($"{prefix}.matchMode deve ser first ou single.");
            }

            return;
        }

        if (!string.Equals(
            condition.Type,
            "value",
            StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}.type deve ser value ou element.");
            return;
        }

        RequireExclusive(
            condition.LeftValue,
            condition.LeftSource,
            $"{prefix}.leftValue",
            $"{prefix}.leftSource",
            errors);
        ValidateValueSource(condition.LeftSource, $"{prefix}.leftSource", errors);

        if (!ConditionOperators.Contains(condition.Operator ?? string.Empty))
        {
            errors.Add($"{prefix}.operator não é suportado: '{condition.Operator}'.");
            return;
        }

        var operatorName = condition.Operator!;
        if (operatorName.Equals("isEmpty", StringComparison.OrdinalIgnoreCase) ||
            operatorName.Equals("isNotEmpty", StringComparison.OrdinalIgnoreCase))
        {
            if (HasLiteral(condition.RightValue) ||
                !string.IsNullOrWhiteSpace(condition.RightSource))
            {
                errors.Add(
                    $"{prefix} não deve informar lado direito para {condition.Operator}.");
            }

            return;
        }

        RequireExclusive(
            condition.RightValue,
            condition.RightSource,
            $"{prefix}.rightValue",
            $"{prefix}.rightSource",
            errors);
        ValidateValueSource(condition.RightSource, $"{prefix}.rightSource", errors);
    }

    private static void ValidateSubflowReferencesAndCycles(
        FlowDefinition definition,
        ICollection<string> errors)
    {
        var knownNames = new HashSet<string>(
            definition.Subflows.Keys,
            StringComparer.OrdinalIgnoreCase);
        foreach (var reference in EnumerateSubflowReferences(definition.Actions))
        {
            if (!knownNames.Contains(reference))
            {
                errors.Add($"Subfluxo não encontrado: '{reference}'.");
            }
        }

        foreach (var actions in definition.Subflows.Values)
        {
            foreach (var reference in EnumerateSubflowReferences(actions))
            {
                if (!knownNames.Contains(reference))
                {
                    errors.Add($"Subfluxo não encontrado: '{reference}'.");
                }
            }
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in definition.Subflows.Keys)
        {
            VisitSubflow(name, definition.Subflows, visiting, visited, errors);
        }

        ValidateSubflowCallDepth(definition.Subflows, errors);
    }

    private static void ValidateSubflowCallDepth(
        IReadOnlyDictionary<string, List<FlowActionDefinition>> subflows,
        ICollection<string> errors)
    {
        var deepestSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exceeded = false;
        foreach (var name in subflows.Keys)
        {
            VisitSubflowDepth(
                name,
                1,
                subflows,
                deepestSeen,
                visiting,
                ref exceeded);
            if (exceeded)
            {
                errors.Add(
                    $"A cadeia de subfluxos ultrapassa o limite de " +
                    $"{MaximumNestingDepth} chamadas aninhadas.");
                return;
            }
        }
    }

    private static void VisitSubflowDepth(
        string name,
        int depth,
        IReadOnlyDictionary<string, List<FlowActionDefinition>> subflows,
        IDictionary<string, int> deepestSeen,
        ISet<string> visiting,
        ref bool exceeded)
    {
        if (depth > MaximumNestingDepth)
        {
            exceeded = true;
            return;
        }

        if (deepestSeen.TryGetValue(name, out var previousDepth) &&
            previousDepth >= depth)
        {
            return;
        }

        if (!visiting.Add(name))
        {
            return;
        }

        deepestSeen[name] = depth;
        var actions = FindSubflow(subflows, name);
        if (actions is not null)
        {
            foreach (var reference in EnumerateSubflowReferences(actions))
            {
                if (FindSubflow(subflows, reference) is not null)
                {
                    VisitSubflowDepth(
                        reference,
                        depth + 1,
                        subflows,
                        deepestSeen,
                        visiting,
                        ref exceeded);
                    if (exceeded)
                    {
                        break;
                    }
                }
            }
        }

        visiting.Remove(name);
    }

    private static void VisitSubflow(
        string name,
        IReadOnlyDictionary<string, List<FlowActionDefinition>> subflows,
        ISet<string> visiting,
        ISet<string> visited,
        ICollection<string> errors)
    {
        if (visited.Contains(name))
        {
            return;
        }

        if (!visiting.Add(name))
        {
            errors.Add($"Ciclo detectado entre subfluxos envolvendo '{name}'.");
            return;
        }

        var actions = FindSubflow(subflows, name);
        if (actions is not null)
        {
            foreach (var reference in EnumerateSubflowReferences(actions))
            {
                if (FindSubflow(subflows, reference) is not null)
                {
                    VisitSubflow(reference, subflows, visiting, visited, errors);
                }
            }
        }

        visiting.Remove(name);
        visited.Add(name);
    }

    private static List<FlowActionDefinition>? FindSubflow(
        IReadOnlyDictionary<string, List<FlowActionDefinition>> subflows,
        string name) =>
        subflows.FirstOrDefault(candidate =>
            candidate.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static IEnumerable<string> EnumerateSubflowReferences(
        IEnumerable<FlowActionDefinition> actions)
    {
        foreach (var action in actions)
        {
            if (action.Type.Equals("runSubflow", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(action.Subflow))
            {
                yield return action.Subflow;
            }

            foreach (var reference in EnumerateSubflowReferences(action.Actions))
            {
                yield return reference;
            }

            foreach (var reference in EnumerateSubflowReferences(action.ElseActions))
            {
                yield return reference;
            }
        }
    }

    private static void RequireNestedActions(
        IReadOnlyCollection<FlowActionDefinition> actions,
        string prefix,
        ICollection<string> errors)
    {
        if (actions.Count == 0)
        {
            errors.Add($"{prefix}.actions deve possuir pelo menos uma ação.");
        }
    }

    private static void RequireValue(
        JsonElement value,
        string? valueSource,
        string prefix,
        ICollection<string> errors) =>
        RequireExclusive(value, valueSource, $"{prefix}.value", $"{prefix}.valueSource", errors);

    private static void RequireExclusive(
        JsonElement literal,
        string? source,
        string literalPath,
        string sourcePath,
        ICollection<string> errors)
    {
        var hasLiteral = HasLiteral(literal);
        var hasSource = !string.IsNullOrWhiteSpace(source);
        if (hasLiteral == hasSource)
        {
            errors.Add($"Informe somente {literalPath} ou {sourcePath}.");
        }
    }

    private static void RequireOptionalExclusive(
        JsonElement literal,
        string? source,
        string literalPath,
        string sourcePath,
        ICollection<string> errors)
    {
        if (HasLiteral(literal) && !string.IsNullOrWhiteSpace(source))
        {
            errors.Add($"Informe somente {literalPath} ou {sourcePath}.");
        }
    }

    private static void RequireExclusive(
        string? literal,
        string? source,
        string literalPath,
        string sourcePath,
        ICollection<string> errors)
    {
        var hasLiteral = literal is not null;
        var hasSource = !string.IsNullOrWhiteSpace(source);
        if (hasLiteral == hasSource)
        {
            errors.Add($"Informe somente {literalPath} ou {sourcePath}.");
        }
    }

    private static void RequireOptionalExclusive(
        string? literal,
        string? source,
        string literalPath,
        string sourcePath,
        ICollection<string> errors)
    {
        if (literal is not null && !string.IsNullOrWhiteSpace(source))
        {
            errors.Add($"Informe somente {literalPath} ou {sourcePath}.");
        }
    }

    private static void ValidateValueSource(
        string? valueSource,
        string path,
        ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(valueSource) &&
            !IsDataPath(valueSource))
        {
            errors.Add($"{path} não é suportado: '{valueSource}'.");
        }
    }

    private static void ValidateFrameSelectors(
        IReadOnlyList<string> frameSelectors,
        string path,
        ICollection<string> errors)
    {
        if (frameSelectors.Count > MaximumFrameDepth)
        {
            errors.Add(
                $"{path} deve possuir no máximo {MaximumFrameDepth} seletores, do iframe externo para o interno.");
        }

        for (var index = 0; index < frameSelectors.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(frameSelectors[index]))
            {
                errors.Add($"{path}[{index}] não pode ser vazio.");
            }
        }
    }

    private static void ValidateLocatorText(
        string? scope,
        string? scopeHasText,
        string? scopeHasTextSource,
        string? hasText,
        string? hasTextSource,
        string prefix,
        ICollection<string> errors)
    {
        RequireOptionalExclusive(
            scopeHasText,
            scopeHasTextSource,
            $"{prefix}.scopeHasText",
            $"{prefix}.scopeHasTextSource",
            errors);
        RequireOptionalExclusive(
            hasText,
            hasTextSource,
            $"{prefix}.hasText",
            $"{prefix}.hasTextSource",
            errors);
        ValidateValueSource(
            scopeHasTextSource,
            $"{prefix}.scopeHasTextSource",
            errors);
        ValidateValueSource(hasTextSource, $"{prefix}.hasTextSource", errors);

        if ((scopeHasText is not null ||
             !string.IsNullOrWhiteSpace(scopeHasTextSource)) &&
            string.IsNullOrWhiteSpace(scope))
        {
            errors.Add(
                $"{prefix}.scope é obrigatório quando o texto do escopo é informado.");
        }
    }

    private static bool IsDataPath(string value) =>
        Regex.IsMatch(
            value,
            "^(input|job|config|variables|attachments|runtime|system|loop)" +
            "\\.[A-Za-z][A-Za-z0-9_-]*(\\[[0-9]+\\])?" +
            "(\\.[A-Za-z][A-Za-z0-9_-]*(\\[[0-9]+\\])?)*$",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

    private static bool HasLiteral(JsonElement value) =>
        value.ValueKind != JsonValueKind.Undefined;

    private static void ValidateInputDefinitions(
        IReadOnlyList<FlowInputRequirementDefinition> requirements,
        ICollection<string> errors)
    {
        var supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "any", "string", "number", "boolean", "object", "array", "null"
        };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < requirements.Count; index++)
        {
            var requirement = requirements[index];
            var prefix = $"inputs[{index}]";
            if (string.IsNullOrWhiteSpace(requirement.Path) ||
                !IsDeclaredInputPath(requirement.Path))
            {
                errors.Add(
                    $"{prefix}.path deve usar input.<caminho> ou attachments.<caminho>.");
            }
            else if (!paths.Add(requirement.Path))
            {
                errors.Add($"{prefix}.path está repetido: '{requirement.Path}'.");
            }

            if (!supportedTypes.Contains(requirement.Type))
            {
                errors.Add($"{prefix}.type não é suportado: '{requirement.Type}'.");
            }
        }
    }

    private static bool IsDeclaredInputPath(string path) =>
        (path.StartsWith("input.", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("attachments.", StringComparison.OrdinalIgnoreCase)) &&
        IsDataPath(path);

    private static void ValidateRuntimeTarget(
        string? target,
        string path,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(target) ||
            !target.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase) ||
            !IsDataPath(target) ||
            target.Contains('[', StringComparison.Ordinal))
        {
            errors.Add($"{path} deve usar runtime.<caminho> sem índice de lista.");
        }
    }

    private static bool IsPrefixedIdentifier(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        IsIdentifier(value[prefix.Length..]);

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value[0] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') &&
        value.All(character =>
            character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or
                (>= '0' and <= '9') or '.' or '_' or '-');

    private static bool IsVariableName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(
            value,
            "^[A-Za-z][A-Za-z0-9_-]*$",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

    private static void Require(
        string? value,
        string property,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{property} é obrigatório.");
        }
    }
}
