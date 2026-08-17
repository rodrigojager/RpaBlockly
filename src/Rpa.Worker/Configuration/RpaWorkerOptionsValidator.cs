using System.Net.Mail;
using System.Text.RegularExpressions;
using RpaFlow.Playwright;
using RpaFlow.Packages;
using RpaFlow.Runtime;
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

            if (definition.Package is null)
            {
                errors.Add(
                    $"{prefix}.Package é obrigatório na V2.");
            }
            else
            {
                ValidatePackageReference(definition.Package, prefix, code, errors);
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
        CancellationToken cancellationToken,
        RpaPackageRuntimeRegistry? packageRegistry = null)
    {
        foreach (var (code, definition) in options.Definitions.Where(item => item.Value.Enabled))
        {
            if (packageRegistry is null)
            {
                throw new InvalidOperationException(
                    $"A definição V2 '{code}' exige RpaPackageRuntimeRegistry.");
            }

            var reference = definition.Package!;
            var rpaId = string.IsNullOrWhiteSpace(reference.RpaId)
                ? code
                : reference.RpaId;
            var snapshot = await packageRegistry.ResolveAsync(
                rpaId,
                reference.OriginName,
                string.IsNullOrWhiteSpace(reference.Revision)
                    ? null
                    : new PackageRevision(reference.Revision),
                cancellationToken);
            var flow = snapshot.Flow;
            ValidateOneTimeCodeProviders(options, code, definition, flow);
            var actions = EnumerateActions(flow).Select(FlowActionIdentity.From).ToArray();
            var knownIds = actions
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

            ValidateSafeValidationBoundary(code, definition, actions);

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

    internal static void ValidateSafeValidationBoundary(
        string code,
        RpaDefinitionOptions definition,
        IReadOnlyList<FlowActionIdentity> actions)
    {
        var safeBoundaryActionId = definition.SafeValidationBoundaryActionId?.Trim();
        if (string.IsNullOrWhiteSpace(safeBoundaryActionId))
        {
            return;
        }

        var boundaryAction = actions.FirstOrDefault(action => action.Id.Equals(
            safeBoundaryActionId,
            StringComparison.OrdinalIgnoreCase));
        if (boundaryAction is null)
        {
            throw new InvalidOperationException(
                $"Definitions.{code}.SafeValidationBoundaryActionId referencia " +
                $"a ação inexistente '{safeBoundaryActionId}'.");
        }

        if (boundaryAction.Type.Equals("if", StringComparison.OrdinalIgnoreCase) ||
            boundaryAction.Type.Equals("repeat", StringComparison.OrdinalIgnoreCase) ||
            boundaryAction.Type.Equals("forEach", StringComparison.OrdinalIgnoreCase) ||
            boundaryAction.Type.Equals("runSubflow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Definitions.{code}.SafeValidationBoundaryActionId deve referenciar " +
                $"uma ação-folha, mas '{safeBoundaryActionId}' usa " +
                $"'{boundaryAction.Type}'.");
        }

        if (definition.IrreversibleActionIds.Contains(
                safeBoundaryActionId,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Definitions.{code}.SafeValidationBoundaryActionId não pode " +
                "ser também uma ação irreversível.");
        }
    }

    private static IEnumerable<RpaFlow.Contracts.V2.FlowActionDefinition> EnumerateActions(
        RpaFlow.Contracts.V2.FlowDefinition flow)
    {
        var pending = new Stack<RpaFlow.Contracts.V2.FlowActionDefinition>(
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
        RpaFlow.Contracts.V2.FlowDefinition flow)
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
        ValidateProviderAliases(options, code, definition, aliases);
        ValidateSensitiveMappings(
            code,
            definition,
            SensitiveRuntimeOutputSanitizer.EnumerateOneTimeCodeTargets(flow));
    }

    private static void ValidatePackageReference(
        RpaPackageReferenceOptions package,
        string prefix,
        string code,
        ICollection<string> errors)
    {
        var rpaId = string.IsNullOrWhiteSpace(package.RpaId) ? code : package.RpaId;
        if (!LogicalIdentifier.IsMatch(rpaId))
        {
            errors.Add($"{prefix}.Package.RpaId possui um identificador inválido.");
        }

        ValidatePackageStoreReference(
            package.OriginName,
            package.Provider,
            package.Location,
            $"{prefix}.Package",
            errors);

        if (!string.IsNullOrWhiteSpace(package.Revision) &&
            (package.Revision.Length != 64 ||
             package.Revision.Any(character => !Uri.IsHexDigit(character))))
        {
            errors.Add(
                $"{prefix}.Package.Revision deve conter um SHA-256 hexadecimal de 64 caracteres.");
        }

        if (package.Overlay is null)
        {
            return;
        }

        ValidatePackageStoreReference(
            package.Overlay.OriginName,
            package.Overlay.Provider,
            package.Overlay.Location,
            $"{prefix}.Package.Overlay",
            errors);
        if (package.OriginName.Equals(
                package.Overlay.OriginName,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"{prefix}.Package.Overlay.OriginName deve ser diferente da origem principal.");
        }
    }

    private static void ValidatePackageStoreReference(
        string originName,
        string provider,
        string location,
        string prefix,
        ICollection<string> errors)
    {
        if (!LogicalIdentifier.IsMatch(originName))
        {
            errors.Add($"{prefix}.OriginName possui um identificador inválido.");
        }

        if (!provider.Equals("File", StringComparison.OrdinalIgnoreCase) &&
            !provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}.Provider deve ser 'File' ou 'SqlServer'.");
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            errors.Add($"{prefix}.Location é obrigatório.");
        }
    }

    private static void ValidateProviderAliases(
        RpaWorkerOptions options,
        string code,
        RpaDefinitionOptions definition,
        IReadOnlyCollection<string> aliases)
    {
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

        if (definition.ClaimEnabled && aliases.Count > 0 && options.MaxParallelism != 1)
        {
            throw new InvalidOperationException(
                $"A definição '{code}' usa código de uso único por e-mail e exige " +
                "RpaWorker:MaxParallelism igual a 1 para não correlacionar mensagens entre casos.");
        }
    }

    private static void ValidateSensitiveMappings(
        string code,
        RpaDefinitionOptions definition,
        IReadOnlySet<string> sensitiveTargets)
    {
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
                BusySelectors: runtime.BusySelectors,
                MaximumArtifactBytes: runtime.MaximumArtifactBytes,
                MaximumArtifactFilesPerExecution:
                    runtime.MaximumArtifactFilesPerExecution,
                ArtifactRetentionDays: runtime.ArtifactRetentionDays));
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
