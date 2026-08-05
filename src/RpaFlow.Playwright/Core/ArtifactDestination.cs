namespace RpaFlow.Playwright;

public sealed record ArtifactDestination(
    string? Directory = null,
    string? FileName = null,
    bool SeparateByExecution = true,
    ArtifactConflictStrategy ConflictStrategy = ArtifactConflictStrategy.Unique);

public enum ArtifactConflictStrategy
{
    Unique,
    Fail,
    Overwrite
}
