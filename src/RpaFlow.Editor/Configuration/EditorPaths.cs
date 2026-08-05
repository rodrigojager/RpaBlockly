namespace RpaFlow.Editor.Configuration;

public sealed record EditorPaths(
    string ProjectRoot,
    string ProfileFile,
    EditorProfile Profile,
    string ConfigurationFile,
    string FlowFile);
