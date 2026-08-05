namespace RpaFlow.Runtime;

public static class FlowExecutionRequestValidator
{
    public static void Validate(FlowExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExecutionId))
        {
            throw new InvalidOperationException("ExecutionId é obrigatório.");
        }

        ArgumentNullException.ThrowIfNull(request.Input);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        ArgumentNullException.ThrowIfNull(request.Attachments);
    }
}
