using System.Text.Json;
using RpaFlow.Contracts;
using RpaFlow.Contracts.V2;
using RpaFlow.Packages;
using V1Action = RpaFlow.Contracts.FlowActionDefinition;
using V1Condition = RpaFlow.Contracts.FlowConditionDefinition;
using V1Flow = RpaFlow.Contracts.FlowDefinition;
using V1FlowValidator = RpaFlow.Contracts.FlowDefinitionValidator;
using V2Action = RpaFlow.Contracts.V2.FlowActionDefinition;
using V2Condition = RpaFlow.Contracts.V2.FlowConditionDefinition;
using V2Flow = RpaFlow.Contracts.V2.FlowDefinition;
using V2Input = RpaFlow.Contracts.V2.FlowInputRequirementDefinition;

namespace RpaFlow.Migrator;

public sealed record MigrationLocatorReport(
    string ActionId,
    string Role,
    string LocatorId,
    string Cardinality);

public sealed record MigrationReport(
    int SchemaVersion,
    string SourceName,
    int ActionCount,
    int LocatorCount,
    int CollectionCount,
    int FirstCardinalityCount,
    IReadOnlyList<MigrationLocatorReport> LocatorUses,
    IReadOnlyList<string> PossibleSemanticDuplicates,
    IReadOnlyList<string> HumanReview);

public sealed record MigrationResult(
    RpaPackageDocuments Documents,
    MigrationReport Report);

public sealed class V1ToV2Migrator
{
    private readonly List<LocatorDefinition> _locators = [];
    private readonly List<MigrationLocatorReport> _uses = [];
    private readonly List<string> _humanReview = [];
    private int _actions;

    public MigrationResult Migrate(V1Flow source, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(source);
        V1FlowValidator.Validate(source);
        _locators.Clear();
        _uses.Clear();
        _humanReview.Clear();
        _actions = 0;

        var flow = new V2Flow
        {
            SchemaVersion = 2,
            Name = source.Name,
            Inputs = source.Inputs.Select(input => new V2Input
            {
                Path = input.Path,
                Type = input.Type,
                Required = input.Required
            }).ToList(),
            Actions = ConvertActions(source.Actions),
            Subflows = source.Subflows.ToDictionary(
                item => item.Key,
                item => ConvertActions(item.Value),
                StringComparer.OrdinalIgnoreCase)
        };
        var documents = new RpaPackageDocuments(
            flow,
            new LocatorCatalog { SchemaVersion = 1, Locators = _locators },
            new RpaPolicyDefinition
            {
                SchemaVersion = 1,
                LocatorResilience = new LocatorResiliencePolicy
                {
                    Mode = LocatorResilienceMode.Strict,
                    LearningWriteBack = LearningWriteBackMode.Disabled,
                    Promotion = LocatorPromotionMode.Disabled,
                    FailedPrimary = FailedPrimaryBehavior.Keep
                }
            });
        RpaPackageValidator.Validate(documents);

        var duplicates = _locators
            .GroupBy(
                locator => V2JsonSerializer.Serialize(locator.Candidates[0].Recipe),
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(item => item.Id)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var report = new MigrationReport(
            1,
            Path.GetFileName(sourceName),
            _actions,
            _locators.Count,
            _uses.Count(item => item.Cardinality == "many"),
            _uses.Count(item => item.Cardinality == "first"),
            _uses.ToArray(),
            duplicates,
            _humanReview.Distinct(StringComparer.Ordinal).OrderBy(
                item => item,
                StringComparer.Ordinal).ToArray());
        return new MigrationResult(documents, report);
    }

    private List<V2Action> ConvertActions(IReadOnlyList<V1Action> actions) =>
        actions.Select(ConvertAction).ToList();

    private V2Action ConvertAction(V1Action source)
    {
        _actions++;
        var action = new V2Action
        {
            Id = source.Id,
            Type = source.Type,
            Name = source.Name,
            Value = Clone(source.Value),
            ValueSource = source.ValueSource,
            ProviderAlias = source.ProviderAlias,
            NotBeforeSource = source.NotBeforeSource,
            Output = source.Target,
            Property = source.Property,
            Attribute = source.Attribute,
            State = source.State,
            Comparison = source.Comparison,
            Operation = source.Operation,
            OptionMode = source.OptionMode,
            SuccessText = source.SuccessText,
            ProtocolPattern = source.ProtocolPattern,
            CompletionTarget = source.CompletionTarget,
            ConfirmationMessageTarget = source.ConfirmationMessageTarget,
            ProtocolTarget = source.ProtocolTarget,
            ScreenshotName = source.ScreenshotName,
            DestinationDirectory = source.DestinationDirectory,
            DestinationDirectorySource = source.DestinationDirectorySource,
            FileName = source.FileName,
            FileNameSource = source.FileNameSource,
            SeparateByExecution = source.SeparateByExecution,
            ConflictStrategy = source.ConflictStrategy,
            DownloadMode = source.DownloadMode,
            Method = source.Method,
            BodyType = source.BodyType,
            RequestBody = Clone(source.RequestBody),
            RequestBodySource = source.RequestBodySource,
            RequestHeaders = Clone(source.RequestHeaders),
            RequestHeadersSource = source.RequestHeadersSource,
            TimeoutMs = source.TimeoutMs,
            PollIntervalMs = source.PollIntervalMs,
            DelayMs = source.DelayMs,
            DecimalPlaces = source.DecimalPlaces,
            MaxItems = source.MaxItems,
            CommitKey = source.CommitKey,
            ClearFirst = source.ClearFirst,
            BlurAfter = source.BlurAfter,
            Optional = source.Optional,
            Times = source.Times,
            TimesSource = source.TimesSource,
            Items = source.Items?.Select(Clone).ToList(),
            ItemsSource = source.ItemsSource,
            ItemVariable = source.ItemVariable,
            IndexVariable = source.IndexVariable,
            Subflow = source.Subflow,
            Actions = ConvertActions(source.Actions),
            ElseActions = ConvertActions(source.ElseActions)
        };

        if (!string.IsNullOrWhiteSpace(source.Selector))
        {
            action.Target = AddLocator(
                source.Id,
                "target",
                CreateRecipe(
                    source.Selector,
                    source.Scope,
                    source.ScopeHasText,
                    source.ScopeHasTextSource,
                    source.HasText,
                    source.HasTextSource,
                    source.FrameSelectors),
                TargetCardinality(source));
        }
        action.Trigger = AddAuxiliary(source.Id, "trigger", source.TriggerSelector, "single");
        action.Options = AddAuxiliary(source.Id, "options", source.OptionSelector, "many");
        action.Ready = AddAuxiliary(source.Id, "ready", source.ReadySelector, "single");
        action.Success = AddAuxiliary(source.Id, "success", source.SuccessSelector, "single");
        action.Protocol = AddAuxiliary(source.Id, "protocol", source.ProtocolSelector, "single");
        if (source.Condition is not null)
        {
            action.Condition = ConvertCondition(source.Id, source.Condition);
        }

        if (source.Type.Equals("safeFinalConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            _humanReview.Add(
                $"{source.Id}: revisar manualmente a semântica de confirmação final antes da produção.");
        }
        return action;
    }

    private V2Condition ConvertCondition(string actionId, V1Condition source)
    {
        var result = new V2Condition
        {
            Type = source.Type,
            Operator = source.Operator,
            LeftValue = Clone(source.LeftValue),
            LeftSource = source.LeftSource,
            RightValue = Clone(source.RightValue),
            RightSource = source.RightSource,
            IgnoreCase = source.IgnoreCase,
            State = source.State
        };
        if (!string.IsNullOrWhiteSpace(source.Selector))
        {
            result.Locator = AddLocator(
                actionId,
                "condition",
                CreateRecipe(
                    source.Selector,
                    source.Scope,
                    source.ScopeHasText,
                    source.ScopeHasTextSource,
                    source.HasText,
                    source.HasTextSource,
                    source.FrameSelectors),
                Cardinality(source.MatchMode, many: false));
        }
        return result;
    }

    private LocatorUseDefinition? AddAuxiliary(
        string actionId,
        string role,
        string? selector,
        string cardinality) =>
        string.IsNullOrWhiteSpace(selector)
            ? null
            : AddLocator(
                actionId,
                role,
                CreateRecipe(selector, null, null, null, null, null, []),
                cardinality);

    private LocatorUseDefinition AddLocator(
        string actionId,
        string role,
        LocatorRecipe recipe,
        string cardinality)
    {
        var locatorId = $"{actionId}.{role}";
        _locators.Add(new LocatorDefinition
        {
            Id = locatorId,
            DisplayName = $"{actionId} · {role}",
            Candidates =
            [
                new LocatorCandidate
                {
                    Id = $"{locatorId}.original",
                    Origin = LocatorCandidateOrigin.Developer,
                    DeveloperRole = DeveloperLocatorRole.Original,
                    OriginalOrder = 0,
                    Recipe = recipe
                }
            ]
        });
        _uses.Add(new MigrationLocatorReport(actionId, role, locatorId, cardinality));
        return new LocatorUseDefinition
        {
            LocatorId = locatorId,
            Cardinality = Enum.Parse<LocatorCardinality>(cardinality, ignoreCase: true)
        };
    }

    private static LocatorRecipe CreateRecipe(
        string target,
        string? scope,
        string? scopeLiteral,
        string? scopeSource,
        string? targetLiteral,
        string? targetSource,
        IReadOnlyList<string> frames) =>
        new()
        {
            Frames = frames.Select(selector => Raw(selector)).ToList(),
            Scope = string.IsNullOrWhiteSpace(scope)
                ? null
                : Raw(scope, Constraint(scopeLiteral, scopeSource)),
            Target = Raw(target, Constraint(targetLiteral, targetSource))
        };

    private static LocatorExpression Raw(
        string selector,
        LocatorTextConstraint? text = null) => new()
        {
            Strategy = LocatorStrategy.RawPlaywright,
            Selector = selector,
            HasText = text
        };

    private static LocatorTextConstraint? Constraint(string? literal, string? source) =>
        string.IsNullOrWhiteSpace(literal) && string.IsNullOrWhiteSpace(source)
            ? null
            : new LocatorTextConstraint { Literal = literal, Source = source };

    private static string TargetCardinality(V1Action action) =>
        action.Type is "readElements" or "typeAcrossInputs"
            ? "many"
            : Cardinality(action.MatchMode, many: false);

    private static string Cardinality(string? matchMode, bool many) =>
        many ? "many" : string.Equals(matchMode, "single", StringComparison.OrdinalIgnoreCase)
            ? "single"
            : "first";

    private static JsonElement Clone(JsonElement value) =>
        value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
}
