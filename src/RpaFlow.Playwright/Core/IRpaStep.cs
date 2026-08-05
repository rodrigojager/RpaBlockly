namespace RpaFlow.Playwright;

public interface IRpaStep
{
    string Name { get; }

    Task ExecuteAsync(RpaContext context, CancellationToken cancellationToken);
}
