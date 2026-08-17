using System.Diagnostics;

namespace RpaFlow.Playwright;

public sealed class JsonFlowActionStep : IRpaStep
{
    private readonly FlowActionDefinition _action;
    private readonly IReadOnlyDictionary<string, List<FlowActionDefinition>> _subflows;
    private readonly int _subflowDepth;
    private readonly FlowActionHandlerRegistry _handlers;

    public JsonFlowActionStep(
        FlowActionDefinition action,
        IReadOnlyDictionary<string, List<FlowActionDefinition>>? subflows = null,
        int subflowDepth = 0,
        FlowActionHandlerRegistry? handlers = null)
    {
        _action = action;
        _subflows = subflows ??
            new Dictionary<string, List<FlowActionDefinition>>(
                StringComparer.OrdinalIgnoreCase);
        _subflowDepth = subflowDepth;
        _handlers = handlers ?? FlowActionHandlerRegistry.Default;
    }

    public string Name => _action.Name;

    public async Task ExecuteAsync(
        RpaContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.ExecutionBudget.Consume(_action.Name);
        var stopwatch = Stopwatch.StartNew();
        var execution = new FlowActionExecutionScope(
            context,
            _subflows,
            _subflowDepth,
            _handlers);
        try
        {
            await context.GuardBeforeActionAsync(_action, cancellationToken);
            await context.ObserveAsync(
                CreateEvent(
                    "actionStarted",
                    context,
                    context.ExecutionBudget.ExecutedActions),
                cancellationToken);
            await _handlers.ExecuteAsync(_action, execution, cancellationToken);
            var directive = await context.GuardAfterActionAsync(
                _action,
                cancellationToken);
            if (directive is not FlowActionExecutionDirective.Continue and
                not FlowActionExecutionDirective.CompleteExecution)
            {
                throw new InvalidOperationException(
                    $"O guard retornou uma diretiva desconhecida para a ação '{_action.Id}'.");
            }

            await context.ObserveAsync(
                CreateEvent(
                    "actionCompleted",
                    context,
                    context.ExecutionBudget.ExecutedActions,
                    stopwatch.ElapsedMilliseconds),
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
                    context.ExecutionBudget.ExecutedActions,
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
                _action,
                context.Page.Url,
                exception);
            await context.ObserveAsync(
                CreateEvent(
                    "actionFailed",
                    context,
                    context.ExecutionBudget.ExecutedActions,
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
        int executedActions,
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
            executedActions,
            elapsedMilliseconds,
            failureCategory,
            retryable);
}
