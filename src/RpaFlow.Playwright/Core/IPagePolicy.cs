using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public interface IPagePolicyFactory
{
    IPagePolicy Create(IPage page);
}

public interface IPagePolicy : IDisposable
{
    Task ExecuteSafeFinalConfirmationAsync(
        FlowActionDefinition action,
        RpaContext context,
        CancellationToken cancellationToken);
}

public sealed class DefaultPagePolicyFactory : IPagePolicyFactory
{
    public static DefaultPagePolicyFactory Instance { get; } = new();

    public IPagePolicy Create(IPage page) => new DefaultPagePolicy();

    private sealed class DefaultPagePolicy : IPagePolicy
    {
        public Task ExecuteSafeFinalConfirmationAsync(
            FlowActionDefinition action,
            RpaContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "safeFinalConfirmation exige uma política específica do sistema de destino.");

        public void Dispose()
        {
        }
    }
}
