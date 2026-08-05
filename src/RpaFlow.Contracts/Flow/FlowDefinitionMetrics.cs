namespace RpaFlow.Contracts;

public static class FlowDefinitionMetrics
{
    public static int CountStructuralActions(FlowDefinition definition)
    {
        var count = 0;
        var pending = new Stack<FlowActionDefinition>(
            definition.Actions.Concat(definition.Subflows.Values.SelectMany(actions => actions)));
        while (pending.TryPop(out var action))
        {
            count++;
            foreach (var nested in action.Actions)
            {
                pending.Push(nested);
            }

            foreach (var nested in action.ElseActions)
            {
                pending.Push(nested);
            }
        }

        return count;
    }
}
