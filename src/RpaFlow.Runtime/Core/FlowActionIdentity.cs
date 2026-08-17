namespace RpaFlow.Runtime;

public sealed record FlowActionIdentity(string Id, string Type, string Name)
{
    public static FlowActionIdentity From(
        global::RpaFlow.Contracts.V2.FlowActionDefinition action) =>
        new(action.Id, action.Type, action.Name);
}
