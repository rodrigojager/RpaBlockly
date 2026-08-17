using RpaFlow.Contracts.V2;
using RpaFlow.Runtime.V2;
using FlowActionDefinition = RpaFlow.Contracts.V2.FlowActionDefinition;
using FlowDefinitionValidator = RpaFlow.Contracts.V2.FlowDefinitionValidator;

namespace RpaFlow.Playwright.V2;

internal sealed class V2ControlFlowActionHandler : IV2FlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "if", "repeat", "forEach", "runSubflow"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "if":
                var result = await V2ConditionEvaluator.EvaluateAsync(
                    action.Condition!,
                    execution,
                    cancellationToken);
                Console.WriteLine(
                    $"  Condição '{action.Name}': {(result ? "verdadeira" : "falsa")}.");
                await execution.ExecuteNestedActionsAsync(
                    result ? action.Actions : action.ElseActions,
                    cancellationToken);
                return;
            case "repeat":
                await RepeatAsync(action, execution, cancellationToken);
                return;
            case "foreach":
                await ForEachAsync(action, execution, cancellationToken);
                return;
            case "runsubflow":
                await SubflowAsync(action, execution, cancellationToken);
                return;
            default:
                throw new InvalidOperationException(
                    $"O handler V2 de controle não interpreta '{action.Type}'.");
        }
    }

    private static async Task RepeatAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var count = V2FlowValueResolver.ResolveIterationCount(
            action,
            execution.Context.Data);
        Console.WriteLine($"  Repetição '{action.Name}': {count} iterações.");
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var indexScope = execution.Context.Data.PushLoopIndex(
                action.IndexVariable ?? "repeatIndex",
                index);
            await execution.ExecuteNestedActionsAsync(action.Actions, cancellationToken);
        }
    }

    private static async Task ForEachAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var items = V2FlowValueResolver.ResolveList(action, execution.Context.Data);
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
            await execution.ExecuteNestedActionsAsync(action.Actions, cancellationToken);
        }
    }

    private static async Task SubflowAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        if (execution.SubflowDepth >= FlowDefinitionValidator.MaximumNestingDepth)
        {
            throw new InvalidOperationException(
                $"O subfluxo '{action.Subflow}' ultrapassou o limite de chamadas aninhadas.");
        }

        var subflow = execution.Subflows.FirstOrDefault(candidate =>
            candidate.Key.Equals(action.Subflow, StringComparison.OrdinalIgnoreCase));
        if (subflow.Key is null)
        {
            throw new InvalidOperationException($"Subfluxo não encontrado: '{action.Subflow}'.");
        }

        Console.WriteLine($"  Executando subfluxo: {subflow.Key}.");
        await execution.ExecuteNestedActionsAsync(
            subflow.Value,
            cancellationToken,
            execution.SubflowDepth + 1);
    }
}
