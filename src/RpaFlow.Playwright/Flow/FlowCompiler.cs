namespace RpaFlow.Playwright;

public static class FlowCompiler
{
    public static IRpaStep[] Compile(FlowDefinition definition)
    {
        FlowDefinitionValidator.Validate(definition);
        return definition.Actions
            .Select(action => (IRpaStep)new JsonFlowActionStep(
                action,
                definition.Subflows))
            .ToArray();
    }
}
