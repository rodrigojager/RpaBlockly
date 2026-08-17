namespace RpaFlow.Playwright;

internal sealed class FlowExecutionCompletedSignalException(string actionId) : Exception
{
    public string ActionId { get; } = actionId;
}
