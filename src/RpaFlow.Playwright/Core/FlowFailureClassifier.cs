using Microsoft.Playwright;

namespace RpaFlow.Playwright;

internal static class FlowFailureClassifier
{
    public static FlowExecutionException ForAction(
        FlowExecutionRequest request,
        FlowActionIdentity action,
        string? currentUrl,
        Exception exception)
    {
        var (category, retryable) = Classify(action, exception);
        return new FlowExecutionException(
            new FlowExecutionFailure(
                request.ExecutionId,
                request.WorkItemId,
                request.BatchId,
                category,
                retryable,
                exception.Message,
                action.Id,
                action.Name,
                action.Type,
                currentUrl),
            exception);
    }

    public static FlowExecutionException ForExecution(
        FlowExecutionRequest request,
        Exception exception,
        bool preflight = false)
    {
        var category = preflight
            ? FlowFailureCategory.Validation
            : exception switch
            {
                TimeoutException => FlowFailureCategory.Timeout,
                IOException => FlowFailureCategory.FileSystem,
                PlaywrightException => FlowFailureCategory.ExternalSystem,
                _ => FlowFailureCategory.Unexpected
            };
        var retryable = !preflight && exception is TimeoutException or PlaywrightException;
        return new FlowExecutionException(
            new FlowExecutionFailure(
                request.ExecutionId,
                request.WorkItemId,
                request.BatchId,
                category,
                retryable,
                exception.Message),
            exception);
    }

    private static (FlowFailureCategory Category, bool Retryable) Classify(
        FlowActionIdentity action,
        Exception exception)
    {
        if (action.Type.Equals("fail", StringComparison.OrdinalIgnoreCase))
        {
            return (FlowFailureCategory.BusinessRule, false);
        }

        return exception switch
        {
            TimeoutException => (FlowFailureCategory.Timeout, true),
            FileNotFoundException or DirectoryNotFoundException =>
                (FlowFailureCategory.FileSystem, false),
            IOException => (FlowFailureCategory.FileSystem, true),
            PlaywrightException => (FlowFailureCategory.WebInteraction, true),
            InvalidOperationException => (FlowFailureCategory.WebInteraction, false),
            _ => (FlowFailureCategory.Unexpected, false)
        };
    }
}
