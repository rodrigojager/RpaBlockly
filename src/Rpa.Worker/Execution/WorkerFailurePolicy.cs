using Microsoft.Data.SqlClient;
using Rpa.Worker.Configuration;
using Rpa.Worker.Domain;
using RpaFlow.Runtime;

namespace Rpa.Worker.Execution;

public static class WorkerFailurePolicy
{
    public static WorkerFailureDecision Decide(
        Exception exception,
        RpaWorkItem item,
        RpaDefinitionOptions definition,
        WorkerFlowExecutionObserver? observer,
        bool workerStopping,
        bool leadershipLost)
    {
        var flowFailure = Find<FlowExecutionException>(exception)?.Failure;
        if (Matches(flowFailure?.ActionId, definition.MfaFailureActionIds))
            return Definitive("MFA_REJEITADO", flowFailure?.Message ?? exception.Message);
        if (Matches(flowFailure?.ActionId, definition.AuthenticationFailureActionIds))
            return Definitive("CREDENCIAL_REJEITADA", flowFailure?.Message ?? exception.Message);
        if (observer?.MfaAttemptStarted == true)
            return Definitive("REPETICAO_DE_MFA_BLOQUEADA",
                exception.Message + " Nenhuma nova tentativa automática de MFA será feita.");
        if (observer?.AuthenticationAttemptStarted == true &&
            observer.AuthenticationAttemptCompleted == false)
            return Definitive("REPETICAO_DE_LOGIN_BLOQUEADA",
                exception.Message + " A autenticação foi iniciada, mas o roteiro não alcançou o marcador de conclusão.");

        if (leadershipLost)
            return Retry("TRAVA_GLOBAL_PERDIDA", exception.Message, preserveAttempt: true);
        if (workerStopping)
            return Retry("WORKER_ENCERRADO", exception.Message, preserveAttempt: true);
        if (flowFailure is not null)
            return flowFailure.Retryable && item.AttemptCount < item.MaxAttempts
                ? Retry("FALHA_TRANSITORIA", flowFailure.Message)
                : Definitive(flowFailure.Retryable ? "TENTATIVAS_ESGOTADAS" : "FALHA_NAO_REPROCESSAVEL", flowFailure.Message);
        if (Find<TimeoutException>(exception) is not null ||
            Find<SqlException>(exception) is not null || exception is IOException)
            return item.AttemptCount < item.MaxAttempts
                ? Retry("FALHA_TRANSITORIA", exception.Message)
                : Definitive("TENTATIVAS_ESGOTADAS", exception.Message);
        if (exception is OperationCanceledException)
            return item.AttemptCount < item.MaxAttempts
                ? Retry("TIMEOUT_OU_CANCELAMENTO_TRANSITORIO", exception.Message)
                : Definitive("TENTATIVAS_ESGOTADAS", exception.Message);
        return Definitive("FALHA_NAO_REPROCESSAVEL", exception.Message);
    }

    private static bool Matches(string? actionId, IEnumerable<string> ids) =>
        actionId is not null && ids.Contains(actionId, StringComparer.OrdinalIgnoreCase);

    private static WorkerFailureDecision Retry(string code, string message, bool preserveAttempt = false) =>
        new("Retry", code, message, true, preserveAttempt);

    private static WorkerFailureDecision Definitive(string code, string message) =>
        new("Failed", code, message, false);

    private static T? Find<T>(Exception exception) where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is T found) return found;
        return null;
    }
}
