using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public sealed class FlowActionExecutionScope
{
    private readonly FlowActionHandlerRegistry _handlers;

    internal FlowActionExecutionScope(
        RpaContext context,
        IReadOnlyDictionary<string, List<FlowActionDefinition>> subflows,
        int subflowDepth,
        FlowActionHandlerRegistry handlers)
    {
        Context = context;
        Subflows = subflows;
        SubflowDepth = subflowDepth;
        _handlers = handlers;
    }

    public RpaContext Context { get; }

    public IReadOnlyDictionary<string, List<FlowActionDefinition>> Subflows { get; }

    public int SubflowDepth { get; }

    public ILocator CreateLocator(FlowActionDefinition action) =>
        FlowLocatorFactory.Create(Context.Page, action, Context.Data);

    public async Task ExecuteNestedActionsAsync(
        IReadOnlyList<FlowActionDefinition> actions,
        CancellationToken cancellationToken,
        int? subflowDepth = null)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"    [{index + 1}/{actions.Count}] {actions[index].Name}");
            var step = new JsonFlowActionStep(
                actions[index],
                Subflows,
                subflowDepth ?? SubflowDepth,
                _handlers);
            await step.ExecuteAsync(Context, cancellationToken);
        }
    }
}
