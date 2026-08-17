namespace RpaFlow.Contracts;

using V2ActionDefinition = V2.FlowActionDefinition;
using V2FlowDefinition = V2.FlowDefinition;

public static class FlowCapabilities
{
    public const string Web = "web";
    public const string FileSystem = "filesystem";
    public const string Http = "http";
    public const string OneTimeCode = "oneTimeCode";
    public const string SafeFinalConfirmation = "safeFinalConfirmation";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        new[] { Web, FileSystem, Http, OneTimeCode, SafeFinalConfirmation },
        StringComparer.OrdinalIgnoreCase);
}

public static class FlowActionCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> CapabilitiesByType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["navigate"] = [FlowCapabilities.Web],
            ["click"] = [FlowCapabilities.Web],
            ["clickIfVisible"] = [FlowCapabilities.Web],
            ["wait"] = [FlowCapabilities.Web],
            ["fill"] = [FlowCapabilities.Web],
            ["selectOption"] = [FlowCapabilities.Web],
            ["setChecked"] = [FlowCapabilities.Web],
            ["pressKey"] = [FlowCapabilities.Web],
            ["typeSequentially"] = [FlowCapabilities.Web],
            ["typeAcrossInputs"] = [FlowCapabilities.Web],
            ["clickAndSwitchPage"] = [FlowCapabilities.Web],
            ["upload"] = [FlowCapabilities.Web, FlowCapabilities.FileSystem],
            ["waitStable"] = [FlowCapabilities.Web],
            ["preserveOrFill"] = [FlowCapabilities.Web],
            ["select2"] = [FlowCapabilities.Web],
            ["fillMaskedCurrency"] = [FlowCapabilities.Web],
            ["fail"] = [],
            ["transformPath"] = [],
            ["captureTimestamp"] = [],
            ["waitForOneTimeCode"] = [FlowCapabilities.OneTimeCode],
            ["setVariable"] = [],
            ["readElement"] = [FlowCapabilities.Web],
            ["readElements"] = [FlowCapabilities.Web],
            ["switchPage"] = [FlowCapabilities.Web],
            ["closePage"] = [FlowCapabilities.Web],
            ["download"] = [FlowCapabilities.Web, FlowCapabilities.FileSystem],
            ["screenshot"] = [FlowCapabilities.Web, FlowCapabilities.FileSystem],
            ["safeFinalConfirmation"] =
                [FlowCapabilities.Web, FlowCapabilities.SafeFinalConfirmation],
            ["if"] = [],
            ["repeat"] = [],
            ["forEach"] = [],
            ["runSubflow"] = []
        };

    public static IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(CapabilitiesByType.Keys, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> RequiredCapabilities(
        V2ActionDefinition action)
    {
        if (!CapabilitiesByType.TryGetValue(action.Type, out var capabilities))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new HashSet<string>(capabilities, StringComparer.OrdinalIgnoreCase);
        if (action.Type.Equals("download", StringComparison.OrdinalIgnoreCase) &&
            action.DownloadMode?.Equals("request", StringComparison.OrdinalIgnoreCase) == true)
        {
            result.Add(FlowCapabilities.Http);
        }

        return result;
    }

    public static IReadOnlySet<string> RequiredCapabilities(V2FlowDefinition definition)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in EnumerateActions(definition))
        {
            result.UnionWith(RequiredCapabilities(action));
        }

        return result;
    }

    private static IEnumerable<V2ActionDefinition> EnumerateActions(
        V2FlowDefinition definition)
    {
        var pending = new Stack<V2ActionDefinition>(
            definition.Actions.Concat(definition.Subflows.Values.SelectMany(actions => actions)));
        while (pending.TryPop(out var action))
        {
            yield return action;
            foreach (var nested in action.Actions)
            {
                pending.Push(nested);
            }

            foreach (var nested in action.ElseActions)
            {
                pending.Push(nested);
            }
        }
    }
}
