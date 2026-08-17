using System.Diagnostics;
using Microsoft.Playwright;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime;
using FlowActionDefinition = RpaFlow.Contracts.V2.FlowActionDefinition;
using FlowDefinition = RpaFlow.Contracts.V2.FlowDefinition;
using FlowDefinitionValidator = RpaFlow.Contracts.V2.FlowDefinitionValidator;

namespace RpaFlow.Playwright.V2;

public interface IV2FlowActionHandler
{
    IReadOnlySet<string> SupportedTypes { get; }

    Task ExecuteAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken);
}

public sealed class V2FlowActionHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IV2FlowActionHandler> _handlers;

    public V2FlowActionHandlerRegistry(IEnumerable<IV2FlowActionHandler> handlers)
    {
        var byType = new Dictionary<string, IV2FlowActionHandler>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            foreach (var type in handler.SupportedTypes)
            {
                if (string.IsNullOrWhiteSpace(type) || !byType.TryAdd(type, handler))
                {
                    throw new InvalidOperationException(
                        $"Registro V2 duplicado ou vazio para o tipo '{type}'.");
                }
            }
        }

        _handlers = byType;
    }

    public static V2FlowActionHandlerRegistry Default { get; } = CreateDefault();

    public IReadOnlySet<string> SupportedTypes =>
        new HashSet<string>(_handlers.Keys, StringComparer.OrdinalIgnoreCase);

    public Task ExecuteAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken) =>
        _handlers.TryGetValue(action.Type, out var handler)
            ? handler.ExecuteAsync(action, execution, cancellationToken)
            : throw new InvalidOperationException(
                $"Tipo de ação V2 não interpretado: '{action.Type}'.");

    private static V2FlowActionHandlerRegistry CreateDefault()
    {
        var registry = new V2FlowActionHandlerRegistry(
        [
            new V2NavigationActionHandler(),
            new V2FormActionHandler(),
            new V2DataAndArtifactActionHandler(),
            new V2ControlFlowActionHandler()
        ]);
        var missing = RpaFlow.Contracts.FlowActionCatalog.SupportedTypes
            .Except(registry._handlers.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpected = registry._handlers.Keys
            .Except(RpaFlow.Contracts.FlowActionCatalog.SupportedTypes,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0 || unexpected.Length > 0)
        {
            throw new InvalidOperationException(
                "O catálogo e os handlers V2 estão dessincronizados. " +
                $"Ausentes: {string.Join(", ", missing)}. " +
                $"Inesperados: {string.Join(", ", unexpected)}.");
        }

        return registry;
    }
}

public sealed class V2FlowActionExecutionScope
{
    private readonly V2FlowActionHandlerRegistry _handlers;
    private readonly FlowActionIdentity _action;

    internal V2FlowActionExecutionScope(
        RpaContext context,
        IReadOnlyDictionary<string, List<FlowActionDefinition>> subflows,
        int subflowDepth,
        ILocatorResolver locatorResolver,
        V2FlowActionHandlerRegistry handlers,
        FlowActionIdentity action)
    {
        Context = context;
        Subflows = subflows;
        SubflowDepth = subflowDepth;
        LocatorResolver = locatorResolver;
        _handlers = handlers;
        _action = action;
    }

    public RpaContext Context { get; }

    public IReadOnlyDictionary<string, List<FlowActionDefinition>> Subflows { get; }

    public int SubflowDepth { get; }

    public ILocatorResolver LocatorResolver { get; }

    public Task<LocatorResolutionResult> ResolveAsync(
        LocatorUseDefinition use,
        LocatorRequiredState state,
        CancellationToken cancellationToken,
        int? timeoutMs = null,
        bool allowEmpty = false) =>
        LocatorResolver.ResolveAsync(
            Context.Page,
            use,
            Context.Data,
            new LocatorResolutionRequirement(state, allowEmpty),
            TimeSpan.FromMilliseconds(
                timeoutMs ?? Context.Options.ActionTimeoutSeconds * 1_000),
            cancellationToken,
            new LocatorResolutionEventContext(_action));

    public Task<LocatorResolutionResult> ResolveTargetAsync(
        FlowActionDefinition action,
        LocatorRequiredState state,
        CancellationToken cancellationToken,
        bool allowEmpty = false) =>
        ResolveAsync(
            action.Target ?? throw MissingLocator(action, "target"),
            state,
            cancellationToken,
            action.TimeoutMs,
            allowEmpty);

    public async Task ExecuteNestedActionsAsync(
        IReadOnlyList<FlowActionDefinition> actions,
        CancellationToken cancellationToken,
        int? subflowDepth = null)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"    [{index + 1}/{actions.Count}] {actions[index].Name}");
            await new V2FlowActionStep(
                actions[index],
                Subflows,
                LocatorResolver,
                subflowDepth ?? SubflowDepth,
                _handlers).ExecuteAsync(Context, cancellationToken);
        }
    }

    private static InvalidOperationException MissingLocator(
        FlowActionDefinition action,
        string role) =>
        new($"A ação V2 '{action.Name}' não informou o locator de papel '{role}'.");
}

public sealed class V2FlowActionStep : IRpaStep
{
    private readonly FlowActionDefinition _action;
    private readonly IReadOnlyDictionary<string, List<FlowActionDefinition>> _subflows;
    private readonly ILocatorResolver _locatorResolver;
    private readonly int _subflowDepth;
    private readonly V2FlowActionHandlerRegistry _handlers;

    public V2FlowActionStep(
        FlowActionDefinition action,
        IReadOnlyDictionary<string, List<FlowActionDefinition>> subflows,
        ILocatorResolver locatorResolver,
        int subflowDepth = 0,
        V2FlowActionHandlerRegistry? handlers = null)
    {
        _action = action;
        _subflows = subflows;
        _locatorResolver = locatorResolver;
        _subflowDepth = subflowDepth;
        _handlers = handlers ?? V2FlowActionHandlerRegistry.Default;
    }

    public string Name => _action.Name;

    public async Task ExecuteAsync(RpaContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.ExecutionBudget.Consume(_action.Name);
        var stopwatch = Stopwatch.StartNew();
        var identity = FlowActionIdentity.From(_action);
        var execution = new V2FlowActionExecutionScope(
            context,
            _subflows,
            _subflowDepth,
            _locatorResolver,
            _handlers,
            identity);
        try
        {
            await context.GuardBeforeActionAsync(identity, cancellationToken);
            await context.ObserveAsync(
                CreateEvent("actionStarted", context),
                cancellationToken);
            await _handlers.ExecuteAsync(_action, execution, cancellationToken);
            var directive = await context.GuardAfterActionAsync(identity, cancellationToken);
            if (directive is not FlowActionExecutionDirective.Continue and
                not FlowActionExecutionDirective.CompleteExecution)
            {
                throw new InvalidOperationException(
                    $"O guard retornou uma diretiva desconhecida para a ação '{_action.Id}'.");
            }

            await context.ObserveAsync(
                CreateEvent("actionCompleted", context, stopwatch.ElapsedMilliseconds),
                cancellationToken);
            if (directive == FlowActionExecutionDirective.CompleteExecution)
            {
                throw new FlowExecutionCompletedSignalException(_action.Id);
            }
        }
        catch (FlowExecutionCompletedSignalException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FlowExecutionException exception)
        {
            await context.ObserveAsync(
                CreateEvent(
                    "actionFailed",
                    context,
                    stopwatch.ElapsedMilliseconds,
                    exception.Failure.Category,
                    exception.Failure.Retryable),
                cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            var classified = FlowFailureClassifier.ForAction(
                context.ExecutionRequest,
                identity,
                context.Page.Url,
                exception);
            await context.ObserveAsync(
                CreateEvent(
                    "actionFailed",
                    context,
                    stopwatch.ElapsedMilliseconds,
                    classified.Failure.Category,
                    classified.Failure.Retryable),
                cancellationToken);
            throw classified;
        }
    }

    private FlowExecutionEvent CreateEvent(
        string kind,
        RpaContext context,
        long? elapsedMilliseconds = null,
        FlowFailureCategory? failureCategory = null,
        bool? retryable = null) =>
        new(
            kind,
            context.ExecutionRequest.ExecutionId,
            context.ExecutionRequest.WorkItemId,
            context.ExecutionRequest.BatchId,
            DateTimeOffset.UtcNow,
            _action.Id,
            _action.Name,
            _action.Type,
            context.ExecutionBudget.ExecutedActions,
            elapsedMilliseconds,
            failureCategory,
            retryable);
}

public static class V2FlowCompiler
{
    public static IRpaStep[] Compile(
        FlowDefinition definition,
        ILocatorResolver locatorResolver)
    {
        FlowDefinitionValidator.Validate(definition);
        return definition.Actions
            .Select(action => (IRpaStep)new V2FlowActionStep(
                action,
                definition.Subflows,
                locatorResolver))
            .ToArray();
    }
}
