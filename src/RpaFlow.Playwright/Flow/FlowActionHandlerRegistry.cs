namespace RpaFlow.Playwright;

public sealed class FlowActionHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IFlowActionHandler> _handlers;

    public FlowActionHandlerRegistry(IEnumerable<IFlowActionHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var byType = new Dictionary<string, IFlowActionHandler>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            foreach (var type in handler.SupportedTypes)
            {
                if (string.IsNullOrWhiteSpace(type))
                {
                    throw new InvalidOperationException(
                        $"O handler {handler.GetType().Name} declarou um tipo vazio.");
                }

                if (!byType.TryAdd(type, handler))
                {
                    throw new InvalidOperationException(
                        $"Mais de um handler foi registrado para a ação '{type}'.");
                }
            }
        }

        _handlers = byType;
    }

    public static FlowActionHandlerRegistry Default { get; } = CreateDefault();

    public IReadOnlySet<string> SupportedTypes =>
        new HashSet<string>(_handlers.Keys, StringComparer.OrdinalIgnoreCase);

    public Task ExecuteAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(action.Type, out var handler))
        {
            throw new InvalidOperationException(
                $"Tipo de ação não interpretado: '{action.Type}'.");
        }

        return handler.ExecuteAsync(action, execution, cancellationToken);
    }

    private static FlowActionHandlerRegistry CreateDefault()
    {
        var registry = new FlowActionHandlerRegistry(
        [
            new NavigationActionHandler(),
            new FormActionHandler(),
            new DataAndArtifactActionHandler(),
            new ControlFlowActionHandler()
        ]);
        var missingTypes = FlowActionCatalog.SupportedTypes
            .Except(registry._handlers.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpectedTypes = registry._handlers.Keys
            .Except(FlowActionCatalog.SupportedTypes, StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingTypes.Length > 0 || unexpectedTypes.Length > 0)
        {
            throw new InvalidOperationException(
                "O catálogo e os handlers de ações estão dessincronizados. " +
                $"Ausentes: {string.Join(", ", missingTypes)}. " +
                $"Inesperados: {string.Join(", ", unexpectedTypes)}.");
        }

        return registry;
    }
}
