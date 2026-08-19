namespace RpaFlow.Playwright;

internal sealed class ControlFlowActionHandler : IFlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "if",
            "repeat",
            "forEach",
            "runSubflow",
            "completeAuthenticationAttempt"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "if":
                await ExecuteIfAsync(action, execution, cancellationToken);
                break;
            case "repeat":
                await ExecuteRepeatAsync(action, execution, cancellationToken);
                break;
            case "foreach":
                await ExecuteForEachAsync(action, execution, cancellationToken);
                break;
            case "runsubflow":
                await ExecuteSubflowAsync(action, execution, cancellationToken);
                break;
            case "completeauthenticationattempt":
                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine("  Tentativa de autenticação marcada como concluída.");
                break;
            default:
                throw new InvalidOperationException(
                    $"O handler de controle de fluxo não interpreta '{action.Type}'.");
        }
    }

    private static async Task ExecuteIfAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var result = await FlowConditionEvaluator.EvaluateAsync(
            action.Condition!,
            execution.Context);
        Console.WriteLine(
            $"  Condição '{action.Name}': {(result ? "verdadeira" : "falsa")}.");
        var branch = result ? action.Actions : action.ElseActions;
        await execution.ExecuteNestedActionsAsync(branch, cancellationToken);
    }

    private static async Task ExecuteRepeatAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var count = FlowValueResolver.ResolveIterationCount(
            action,
            execution.Context.Data);
        Console.WriteLine($"  Repetição '{action.Name}': {count} iterações.");
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var indexScope = execution.Context.Data.PushLoopIndex(
                action.IndexVariable ?? "repeatIndex",
                index);
            await execution.ExecuteNestedActionsAsync(
                action.Actions,
                cancellationToken);
        }
    }

    private static async Task ExecuteForEachAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var items = FlowValueResolver.ResolveList(action, execution.Context.Data);
        if (items.Count > FlowDefinitionValidator.MaximumLoopIterations)
        {
            throw new InvalidOperationException(
                $"A lista de '{action.Name}' ultrapassa " +
                $"{FlowDefinitionValidator.MaximumLoopIterations} itens.");
        }

        Console.WriteLine($"  Lista '{action.Name}': {items.Count} itens.");
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var loopScope = execution.Context.Data.PushLoopScope(
                action.ItemVariable!,
                items[index],
                action.IndexVariable ?? $"{action.ItemVariable}Index",
                index);
            await execution.ExecuteNestedActionsAsync(
                action.Actions,
                cancellationToken);
        }
    }

    private static async Task ExecuteSubflowAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        if (execution.SubflowDepth >= FlowDefinitionValidator.MaximumNestingDepth)
        {
            throw new InvalidOperationException(
                $"O subfluxo '{action.Subflow}' ultrapassou o limite de chamadas aninhadas.");
        }

        var subflow = execution.Subflows.FirstOrDefault(candidate =>
            candidate.Key.Equals(
                action.Subflow,
                StringComparison.OrdinalIgnoreCase));
        if (subflow.Key is null)
        {
            throw new InvalidOperationException(
                $"Subfluxo não encontrado: '{action.Subflow}'.");
        }

        Console.WriteLine($"  Executando subfluxo: {subflow.Key}.");
        await execution.ExecuteNestedActionsAsync(
            subflow.Value,
            cancellationToken,
            execution.SubflowDepth + 1);
    }
}
