namespace RpaFlow.Playwright;

public static class ArtifactDestinationResolver
{
    public static ArtifactDestination Resolve(
        RpaFlow.Contracts.V2.FlowActionDefinition action,
        RpaContext context,
        string? fallbackFileName = null)
    {
        var directory = FlowValueResolver.ResolveOptionalText(
            action.DestinationDirectory,
            action.DestinationDirectorySource,
            context.Data);
        var fileName = FlowValueResolver.ResolveOptionalText(
            action.FileName,
            action.FileNameSource,
            context.Data) ?? fallbackFileName;
        var conflict = action.ConflictStrategy?.ToLowerInvariant() switch
        {
            null or "" or "unique" => ArtifactConflictStrategy.Unique,
            "fail" => ArtifactConflictStrategy.Fail,
            "overwrite" => ArtifactConflictStrategy.Overwrite,
            _ => throw new InvalidOperationException(
                $"Estratégia de conflito inválida: '{action.ConflictStrategy}'.")
        };

        return new ArtifactDestination(
            directory,
            fileName,
            action.SeparateByExecution ?? true,
            conflict);
    }
}
