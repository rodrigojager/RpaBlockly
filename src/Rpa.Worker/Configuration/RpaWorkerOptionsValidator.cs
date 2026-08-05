using System.Net.Mail;
using System.Text.RegularExpressions;
using RpaFlow.Contracts;
using RpaFlow.Playwright;
using Rpa.Worker.Execution;

namespace Rpa.Worker.Configuration;

public static class RpaWorkerOptionsValidator
{
    private static readonly Regex SqlIdentifier = new(
        "^[A-Za-z][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex LogicalIdentifier = new(
        "^[A-Za-z][A-Za-z0-9._-]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static WorkerPaths Validate(
        RpaWorkerOptions options,
        string configurationDirectory,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        RequireRange(options.PollIntervalSeconds, 1, 3600, "PollIntervalSeconds", errors);
        RequireRange(options.MaxParallelism, 1, 64, "MaxParallelism", errors);
        RequireRange(options.LeaseSeconds, 30, 86400, "LeaseSeconds", errors);
        RequireRange(options.HeartbeatSeconds, 5, 3600, "HeartbeatSeconds", errors);
        RequireRange(options.CaseTimeoutMinutes, 1, 1440, "CaseTimeoutMinutes", errors);
        RequireRange(options.RetryDelaySeconds, 1, 86400, "RetryDelaySeconds", errors);
        if (options.HeartbeatSeconds >= options.LeaseSeconds)
        {
            errors.Add("HeartbeatSeconds deve ser menor que LeaseSeconds.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkerId))
        {
            errors.Add("WorkerId é obrigatório.");
        }

        ValidateSqlIdentifier(options.Tables.Schema, "Tables.Schema", errors);
        ValidateSqlIdentifier(options.Tables.WorkItems, "Tables.WorkItems", errors);
        ValidateSqlIdentifier(options.Tables.Executions, "Tables.Executions", errors);
        ValidateSqlIdentifier(options.Tables.Outputs, "Tables.Outputs", errors);
        ValidateSqlIdentifier(options.Tables.Artifacts, "Tables.Artifacts", errors);
        ValidateSqlIdentifier(options.Tables.Events, "Tables.Events", errors);
        if (options.EmailReader is null)
        {
            errors.Add("EmailReader é obrigatório.");
        }
        else
        {
            ValidateEmailReader(options.EmailReader, errors);
        }

        var workspaceRoot = ResolvePath(configurationDirectory, options.WorkspaceRoot);
        var artifactRoot = ResolvePath(workspaceRoot, options.Storage.ArtifactRoot);
        var sessionRoot = ResolvePath(workspaceRoot, options.Storage.SessionStateRoot);

        if (options.Definitions.Count == 0)
        {
            errors.Add("Definitions precisa possuir ao menos um RPA.");
        }

        foreach (var (code, definition) in options.Definitions)
        {
            var prefix = $"Definitions.{code}";
            if (!LogicalIdentifier.IsMatch(code))
            {
                errors.Add($"{prefix} possui um código inválido.");
            }

            if (string.IsNullOrWhiteSpace(definition.FlowFile))
            {
                errors.Add($"{prefix}.FlowFile é obrigatório.");
            }

            ValidateRuntime(definition.Runtime, prefix, workspaceRoot, errors);
            ValidateMappings(definition, prefix, errors);
        }

        if (options.Enabled && string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add("ConnectionStrings.RpaDatabase é obrigatória quando o worker está habilitado.");
        }

        if (options.Enabled && !options.Definitions.Any(item =>
                item.Value.Enabled && item.Value.ClaimEnabled))
        {
            errors.Add("Habilite ClaimEnabled em pelo menos uma definição antes de ligar o worker.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Configuração do worker inválida:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
        }

        return new WorkerPaths(
            configurationDirectory,
            workspaceRoot,
            artifactRoot,
            sessionRoot);
    }

    public static async Task ValidateFlowsAsync(
        RpaWorkerOptions options,
        WorkerPaths paths,
        CancellationToken cancellationToken)
    {
        var loader = new JsonFlowLoader();
        foreach (var (code, definition) in options.Definitions.Where(item => item.Value.Enabled))
        {
            var flowPath = ResolvePath(paths.WorkspaceRoot, definition.FlowFile);
            if (!File.Exists(flowPath))
            {
                throw new FileNotFoundException(
                    $"Fluxo da definição '{code}' não encontrado.",
                    flowPath);
            }

            var flow = await loader.LoadAsync(flowPath, cancellationToken);
            ValidateOneTimeCodeProviders(options, code, definition, flow);
            var knownIds = EnumerateActions(flow)
                .Select(action => action.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var actionId in definition.IrreversibleActionIds)
            {
                if (!knownIds.Contains(actionId))
                {
                    throw new InvalidOperationException(
                        $"Definitions.{code}.IrreversibleActionIds referencia a ação inexistente '{actionId}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(definition.ConfigurationFile))
            {
                var configurationPath = ResolvePath(
                    paths.WorkspaceRoot,
                    definition.ConfigurationFile);
                if (!File.Exists(configurationPath))
                {
                    throw new FileNotFoundException(
                        $"Configuração da definição '{code}' não encontrada.",
                        configurationPath);
                }
            }
        }
    }

    public static string ResolvePath(string baseDirectory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

    private static IEnumerable<FlowActionDefinition> EnumerateActions(FlowDefinition flow)
    {
        var pending = new Stack<FlowActionDefinition>(
            flow.Actions.Concat(flow.Subflows.Values.SelectMany(actions => actions)));
        while (pending.TryPop(out var action))
        {
            yield return action;
            foreach (var nested in action.Actions.Concat(action.ElseActions))
            {
                pending.Push(nested);
            }
        }
    }

    internal static void ValidateOneTimeCodeProviders(
        RpaWorkerOptions options,
        string code,
        RpaDefinitionOptions definition,
        FlowDefinition flow)
    {
        var aliases = EnumerateActions(flow)
            .Where(action => action.Type.Equals(
                "waitForOneTimeCode",
                StringComparison.OrdinalIgnoreCase))
            .Select(action => action.ProviderAlias?.Trim())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var alias in aliases)
        {
            if (!options.EmailReader.Providers.TryGetValue(alias, out var provider))
            {
                throw new InvalidOperationException(
                    $"A definição '{code}' usa o provider de código de uso único " +
                    $"'{alias}', mas esse alias não existe em RpaWorker:EmailReader:Providers.");
            }

            if (definition.ClaimEnabled && !provider.Enabled)
            {
                throw new InvalidOperationException(
                    $"A definição '{code}' não pode fazer claim: o provider de código " +
                    $"de uso único '{alias}' está desabilitado.");
            }
        }

        if (definition.ClaimEnabled && aliases.Length > 0 && options.MaxParallelism != 1)
        {
            throw new InvalidOperationException(
                $"A definição '{code}' usa código de uso único por e-mail e exige " +
                "RpaWorker:MaxParallelism igual a 1 para não correlacionar mensagens entre casos.");
        }

        var sensitiveTargets = SensitiveRuntimeOutputSanitizer
            .EnumerateOneTimeCodeTargets(flow);
        foreach (var mapping in definition.Outputs.Select(output =>
                     (Kind: "Outputs", output.Name, output.Source))
                 .Concat(definition.Artifacts.Select(artifact =>
                     (Kind: "Artifacts", artifact.Name, artifact.Source))))
        {
            var sensitiveTarget = sensitiveTargets.FirstOrDefault(target =>
                SensitiveRuntimeOutputSanitizer.PathsOverlap(mapping.Source, target));
            if (sensitiveTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Definitions.{code}.{mapping.Kind}.{mapping.Name}.Source não pode mapear " +
                    $"'{mapping.Source}', pois contém o código temporário de '{sensitiveTarget}'.");
            }
        }
    }

    private static void ValidateRuntime(
        RpaRuntimeOptions runtime,
        string prefix,
        string workspaceRoot,
        ICollection<string> errors)
    {
        try
        {
            PlaywrightRuntimeOptionsValidator.Validate(new PlaywrightRuntimeOptions(
                runtime.Headless,
                runtime.Browser,
                runtime.ActionTimeoutSeconds,
                runtime.UploadTimeoutSeconds,
                Path.Combine(workspaceRoot, "storage", "artifacts"),
                workspaceRoot,
                runtime.Locale,
                runtime.ViewportWidth,
                runtime.ViewportHeight,
                ReadinessQuietPeriodMs: runtime.ReadinessQuietPeriodMs,
                FormStabilityMs: runtime.FormStabilityMs,
                BusySelectors: runtime.BusySelectors));
        }
        catch (Exception exception)
        {
            errors.Add($"{prefix}.Runtime: {exception.Message}");
        }
    }

    private static void ValidateMappings(
        RpaDefinitionOptions definition,
        string prefix,
        ICollection<string> errors)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in definition.Outputs)
        {
            ValidateMapping(output.Name, output.Source, $"{prefix}.Outputs", names, errors);
        }

        names.Clear();
        foreach (var artifact in definition.Artifacts)
        {
            ValidateMapping(
                artifact.Name,
                artifact.Source,
                $"{prefix}.Artifacts",
                names,
                errors);
            if (string.IsNullOrWhiteSpace(artifact.Kind))
            {
                errors.Add($"{prefix}.Artifacts.Kind é obrigatório.");
            }
        }
    }

    private static void ValidateEmailReader(
        MicrosoftGraphEmailReaderOptions emailReader,
        ICollection<string> errors)
    {
        RequireRange(
            emailReader.RequestTimeoutSeconds,
            1,
            300,
            "EmailReader.RequestTimeoutSeconds",
            errors);

        if (emailReader.Providers is null)
        {
            errors.Add("EmailReader.Providers é obrigatório.");
            return;
        }

        var enabledProviderExists = false;
        foreach (var (alias, provider) in emailReader.Providers)
        {
            var prefix = $"EmailReader.Providers.{alias}";
            if (!LogicalIdentifier.IsMatch(alias))
            {
                errors.Add(
                    $"{prefix} usa um alias inválido; use letras, números, ponto, hífen ou sublinhado.");
            }

            RequireRange(
                provider.MaximumEmailAgeMinutes,
                1,
                60,
                $"{prefix}.MaximumEmailAgeMinutes",
                errors);
            RequireRange(
                provider.RequestedEmailCount,
                1,
                50,
                $"{prefix}.RequestedEmailCount",
                errors);

            if (!string.Equals(
                    provider.Provider,
                    EmailOneTimeCodeProviderOptions.MicrosoftGraphProvider,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"{prefix}.Provider deve ser " +
                    $"'{EmailOneTimeCodeProviderOptions.MicrosoftGraphProvider}'.");
            }

            ValidateEmailAddress(provider.Mailbox, $"{prefix}.Mailbox", errors);
            ValidateEmailAddress(provider.SenderAddress, $"{prefix}.SenderAddress", errors);

            if ((provider.SubjectContains?.Length ?? 0) > 200)
            {
                errors.Add($"{prefix}.SubjectContains deve possuir no máximo 200 caracteres.");
            }

            if ((provider.CodePattern?.Length ?? 0) > 2048)
            {
                errors.Add($"{prefix}.CodePattern deve possuir no máximo 2048 caracteres.");
            }
            else if (!string.IsNullOrWhiteSpace(provider.CodePattern))
            {
                try
                {
                    _ = new Regex(
                        provider.CodePattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException)
                {
                    errors.Add(
                        $"{prefix}.CodePattern não contém uma expressão regular válida.");
                }
            }

            if (!provider.Enabled)
            {
                continue;
            }

            enabledProviderExists = true;
            if (string.IsNullOrWhiteSpace(provider.Mailbox))
            {
                errors.Add($"{prefix}.Mailbox é obrigatório quando a captura está habilitada.");
            }

            if (string.IsNullOrWhiteSpace(provider.SubjectContains))
            {
                errors.Add(
                    $"{prefix}.SubjectContains é obrigatório quando a captura está habilitada.");
            }

            if (string.IsNullOrWhiteSpace(provider.CodePattern))
            {
                errors.Add(
                    $"{prefix}.CodePattern é obrigatório quando a captura está habilitada.");
            }
        }

        if (!enabledProviderExists)
        {
            return;
        }

        if (!Guid.TryParse(emailReader.TenantId, out _))
        {
            errors.Add(
                "EmailReader.TenantId deve conter um GUID válido quando a captura está habilitada.");
        }

        if (!Guid.TryParse(emailReader.ClientId, out _))
        {
            errors.Add(
                "EmailReader.ClientId deve conter um GUID válido quando a captura está habilitada.");
        }

        if (string.IsNullOrWhiteSpace(emailReader.ClientSecret))
        {
            errors.Add(
                "EmailReader.ClientSecret é obrigatório quando a captura está habilitada.");
        }
    }

    private static void ValidateEmailAddress(
        string? value,
        string path,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (!MailAddress.TryCreate(trimmed, out var parsed) ||
            !parsed.Address.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{path} não contém um endereço de e-mail válido.");
        }
    }

    private static void ValidateMapping(
        string name,
        string source,
        string prefix,
        ISet<string> names,
        ICollection<string> errors)
    {
        if (!LogicalIdentifier.IsMatch(name))
        {
            errors.Add($"{prefix} contém o nome inválido '{name}'.");
        }
        else if (!names.Add(name))
        {
            errors.Add($"{prefix} repete o nome '{name}'.");
        }

        if (!source.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}.{name}.Source deve usar runtime.<caminho>.");
        }
    }

    private static void ValidateSqlIdentifier(
        string value,
        string path,
        ICollection<string> errors)
    {
        if (!SqlIdentifier.IsMatch(value))
        {
            errors.Add($"{path} deve ser um identificador SQL simples e seguro.");
        }
    }

    private static void RequireRange(
        int value,
        int minimum,
        int maximum,
        string path,
        ICollection<string> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"{path} deve estar entre {minimum} e {maximum}.");
        }
    }
}
