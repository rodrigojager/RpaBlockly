namespace Rpa.Worker.Hosting;

public sealed class WorkerStartupValidationState
{
    private readonly TaskCompletionSource _validated = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilValidatedAsync(CancellationToken cancellationToken) =>
        _validated.Task.WaitAsync(cancellationToken);

    public void MarkValidated() => _validated.TrySetResult();
}
