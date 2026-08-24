namespace RpaFlow.Runtime;

public sealed record FlowInputRequirement(string Path, string Type, bool Required)
{
    public static FlowInputRequirement From(
        global::RpaFlow.Contracts.V2.FlowInputRequirementDefinition requirement) =>
        new(requirement.Path, requirement.Type, requirement.Required);
}
