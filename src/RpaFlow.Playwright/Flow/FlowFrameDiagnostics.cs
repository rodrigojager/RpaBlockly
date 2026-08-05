using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RpaFlow.Playwright;

internal static class FlowFrameDiagnostics
{
    public static async Task ReportTargetMatchesAsync(
        FlowActionDefinition action,
        RpaContext context,
        CancellationToken cancellationToken)
    {
        using var diagnosticSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        diagnosticSource.CancelAfter(TimeSpan.FromSeconds(5));
        var diagnosticToken = diagnosticSource.Token;

        try
        {
            Console.Error.WriteLine(
                $"Diagnóstico de frames para o seletor '{action.Selector}':");
            var frames = context.Page.Frames.ToList();
            for (var index = 0; index < frames.Count; index++)
            {
                diagnosticToken.ThrowIfCancellationRequested();
                var frame = frames[index];
                var locator = FlowLocatorFactory.Create(frame, action, context.Data);
                var count = await locator.CountAsync().WaitAsync(diagnosticToken);
                var visibleCount = 0;
                for (var targetIndex = 0; targetIndex < count; targetIndex++)
                {
                    diagnosticToken.ThrowIfCancellationRequested();
                    if (await locator.Nth(targetIndex)
                            .IsVisibleAsync()
                            .WaitAsync(diagnosticToken))
                    {
                        visibleCount++;
                    }
                }

                var owner = await DescribeOwnerAsync(frame, diagnosticToken);
                var parentIndex = frame.ParentFrame is null
                    ? -1
                    : frames.IndexOf(frame.ParentFrame);
                Console.Error.WriteLine(
                    $"  frame[{index}] pai={parentIndex} nome='{Sanitize(frame.Name)}' " +
                    $"origem='{DescribeUrl(frame.Url)}' owner={owner} " +
                    $"alvos={count} visíveis={visibleCount}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(
                "Diagnóstico auxiliar de frames interrompido após 5 segundos.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Diagnóstico auxiliar de frames falhou ({exception.GetType().Name}).");
        }
    }

    private static async Task<string> DescribeOwnerAsync(
        IFrame frame,
        CancellationToken cancellationToken)
    {
        if (frame.ParentFrame is null)
        {
            return "página";
        }

        var element = await frame.FrameElementAsync().WaitAsync(cancellationToken);
        var id = await element.GetAttributeAsync("id").WaitAsync(cancellationToken) ??
            string.Empty;
        var name = await element.GetAttributeAsync("name").WaitAsync(cancellationToken) ??
            string.Empty;
        var sourceValue =
            await element.GetAttributeAsync("src").WaitAsync(cancellationToken) ??
            string.Empty;
        return $"iframe(id='{Sanitize(id)}', name='{Sanitize(name)}', " +
            $"src='{DescribeUrl(sourceValue)}')";
    }

    private static string DescribeUrl(string value)
    {
        var path = Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path)
            : value.Split(['?', '#'], 2)[0];
        var redacted = Regex.Replace(
            path,
            @";jsessionid=[^/?#]+",
            ";jsessionid=[oculto]",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));
        return Limit(redacted);
    }

    private static string Sanitize(string value)
    {
        var singleLine = value
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        var redacted = Regex.Replace(
            singleLine,
            @"(?i)(token|session|auth|key|code)=([^&;\s]+)",
            "$1=[oculto]",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        return Limit(redacted);
    }

    private static string Limit(string value) =>
        value.Length <= 240 ? value : value[..240] + "…";
}
