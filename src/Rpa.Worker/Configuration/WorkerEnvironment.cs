namespace Rpa.Worker.Configuration;

public sealed record WorkerEnvironment(
    string ConnectionString,
    WorkerPaths Paths);
