namespace RpaFlow.Playwright;

public interface IFlowActionHandler
{
    IReadOnlySet<string> SupportedTypes { get; }

    Task ExecuteAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken);
}
