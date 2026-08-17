namespace RpaFlow.Contracts.V2;

public static class LocatorCatalogValidator
{
    public const int MaximumLocators = 10_000;
    public const int MaximumCandidatesPerLocator = 100;
    public const int MaximumFramesPerRecipe = 16;
    public const int MaximumFingerprintsPerLocator = 20;
    public const int MaximumFingerprintTextLength = 2_000;
    public const int MaximumFingerprintAttributes = 64;
    public const int MaximumFingerprintAncestors = 32;
    public const int MaximumFingerprintSiblingsPerSide = 10;

    private static readonly IReadOnlySet<string> SensitiveAttributeNames =
        new HashSet<string>(
            [
                "value", "password", "passwd", "secret", "token", "authorization",
                "cookie", "set-cookie", "session", "apikey", "api-key"
            ],
            StringComparer.OrdinalIgnoreCase);

    public static void Validate(LocatorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"locators.schemaVersion deve ser 1, mas foi {catalog.SchemaVersion}.");
        }

        if (catalog.Locators.Count > MaximumLocators)
        {
            throw new InvalidOperationException(
                $"O catálogo excedeu {MaximumLocators} localizadores.");
        }

        var locatorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var locator in catalog.Locators)
        {
            RequireText(locator.Id, "locator.id");
            RequireText(locator.DisplayName, $"locators.{locator.Id}.displayName");
            if (!locatorIds.Add(locator.Id))
            {
                throw new InvalidOperationException(
                    $"O locator ID '{locator.Id}' está duplicado.");
            }

            if (locator.Candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"O locator '{locator.Id}' deve possuir ao menos um candidato.");
            }

            if (locator.Candidates.Count > MaximumCandidatesPerLocator)
            {
                throw new InvalidOperationException(
                    $"O locator '{locator.Id}' excedeu {MaximumCandidatesPerLocator} candidatos.");
            }

            ValidateCandidates(locator, candidateIds);
            ValidateFingerprints(locator);
        }
    }

    private static void ValidateCandidates(
        LocatorDefinition locator,
        ISet<string> candidateIds)
    {
        var originalCount = 0;
        var authoredPrimaryCount = 0;
        for (var index = 0; index < locator.Candidates.Count; index++)
        {
            var candidate = locator.Candidates[index];
            var path = $"locators.{locator.Id}.candidates[{index}]";
            RequireText(candidate.Id, $"{path}.id");
            if (!candidateIds.Add(candidate.Id))
            {
                throw new InvalidOperationException(
                    $"O candidate ID '{candidate.Id}' está duplicado.");
            }

            switch (candidate.Origin)
            {
                case LocatorCandidateOrigin.Developer:
                    if (candidate.DeveloperRole is null || candidate.RecorderRole is not null)
                    {
                        throw new InvalidOperationException(
                            $"{path} de origem developer exige apenas developerRole.");
                    }

                    if (candidate.OriginalOrder is < 0)
                    {
                        throw new InvalidOperationException(
                            $"{path}.originalOrder não pode ser negativo.");
                    }

                    if (candidate.DeveloperRole == DeveloperLocatorRole.Original)
                    {
                        originalCount++;
                        authoredPrimaryCount++;
                    }

                    break;
                case LocatorCandidateOrigin.Recorder:
                    if (candidate.RecorderRole is null || candidate.DeveloperRole is not null)
                    {
                        throw new InvalidOperationException(
                            $"{path} de origem recorder exige apenas recorderRole.");
                    }

                    if (candidate.RecorderRole == RecorderLocatorRole.CapturedPrimary)
                    {
                        authoredPrimaryCount++;
                    }

                    break;
                case LocatorCandidateOrigin.Heuristic:
                    if (candidate.DeveloperRole is not null || candidate.RecorderRole is not null)
                    {
                        throw new InvalidOperationException(
                            $"{path} de origem heuristic não aceita papel de autoria.");
                    }

                    break;
            }

            if (index == 0 &&
                ((candidate.Origin == LocatorCandidateOrigin.Developer &&
                  candidate.DeveloperRole != DeveloperLocatorRole.Original) ||
                 (candidate.Origin == LocatorCandidateOrigin.Recorder &&
                  candidate.RecorderRole != RecorderLocatorRole.CapturedPrimary) ||
                 (candidate.Origin == LocatorCandidateOrigin.Heuristic &&
                  candidate.PromotedAtUtc is null)))
            {
                throw new InvalidOperationException(
                    $"{path} deve ser o principal de autoria ou uma promoção confirmada.");
            }

            ValidateRecipe(candidate.Recipe, path);
        }

        if (originalCount > 1)
        {
            throw new InvalidOperationException(
                $"O locator '{locator.Id}' possui mais de um developerRole original.");
        }


        if (authoredPrimaryCount != 1)
        {
            throw new InvalidOperationException(
                $"O locator '{locator.Id}' deve possuir exatamente um principal de autoria.");
        }
    }

    private static void ValidateRecipe(LocatorRecipe recipe, string path)
    {
        if (recipe.Frames.Count > MaximumFramesPerRecipe)
        {
            throw new InvalidOperationException(
                $"{path}.recipe.frames excedeu {MaximumFramesPerRecipe} níveis.");
        }

        for (var index = 0; index < recipe.Frames.Count; index++)
        {
            ValidateExpression(
                recipe.Frames[index],
                $"{path}.recipe.frames[{index}]",
                allowTextFilter: false);
            if (recipe.Frames[index].Strategy == LocatorStrategy.Fingerprint)
            {
                throw new InvalidOperationException(
                    $"{path}.recipe.frames[{index}] não aceita fingerprint.");
            }
        }

        if (recipe.Scope is not null)
        {
            ValidateExpression(recipe.Scope, $"{path}.recipe.scope", allowTextFilter: true);
            if (recipe.Scope.Strategy == LocatorStrategy.Fingerprint)
            {
                throw new InvalidOperationException(
                    $"{path}.recipe.scope não aceita fingerprint.");
            }
        }

        ValidateExpression(recipe.Target, $"{path}.recipe.target", allowTextFilter: true);
    }

    private static void ValidateExpression(
        LocatorExpression expression,
        string path,
        bool allowTextFilter)
    {
        switch (expression.Strategy)
        {
            case LocatorStrategy.Css:
            case LocatorStrategy.XPath:
            case LocatorStrategy.RawPlaywright:
                RequireText(expression.Selector, $"{path}.selector");
                break;
            case LocatorStrategy.Role:
                RequireText(expression.Role, $"{path}.role");
                break;
            case LocatorStrategy.Label:
            case LocatorStrategy.Placeholder:
            case LocatorStrategy.Text:
            case LocatorStrategy.TestId:
                RequireText(expression.Text, $"{path}.text");
                break;
            case LocatorStrategy.Fingerprint:
                RequireText(expression.FingerprintId, $"{path}.fingerprintId");
                break;
            default:
                throw new InvalidOperationException($"{path}.strategy é inválida.");
        }

        ValidateExpressionShape(expression, path);

        if (!allowTextFilter && expression.HasText is not null)
        {
            throw new InvalidOperationException($"{path}.hasText não é permitido em frame.");
        }

        if (expression.HasText is not null)
        {
            var hasLiteral = !string.IsNullOrWhiteSpace(expression.HasText.Literal);
            var hasSource = !string.IsNullOrWhiteSpace(expression.HasText.Source);
            if (hasLiteral == hasSource)
            {
                throw new InvalidOperationException(
                    $"{path}.hasText deve informar literal ou source, exclusivamente.");
            }

            if (hasSource && !DataPath.IsReadable(expression.HasText.Source!))
            {
                throw new InvalidOperationException(
                    $"{path}.hasText.source possui caminho inválido.");
            }
        }
    }

    private static void ValidateExpressionShape(LocatorExpression expression, string path)
    {
        switch (expression.Strategy)
        {
            case LocatorStrategy.Css:
            case LocatorStrategy.XPath:
            case LocatorStrategy.RawPlaywright:
                Reject(expression.Role, $"{path}.role");
                Reject(expression.Name, $"{path}.name");
                Reject(expression.Text, $"{path}.text");
                Reject(expression.FingerprintId, $"{path}.fingerprintId");
                break;
            case LocatorStrategy.Role:
                Reject(expression.Selector, $"{path}.selector");
                Reject(expression.Text, $"{path}.text");
                Reject(expression.FingerprintId, $"{path}.fingerprintId");
                break;
            case LocatorStrategy.Label:
            case LocatorStrategy.Placeholder:
            case LocatorStrategy.Text:
            case LocatorStrategy.TestId:
                Reject(expression.Selector, $"{path}.selector");
                Reject(expression.Role, $"{path}.role");
                Reject(expression.Name, $"{path}.name");
                Reject(expression.FingerprintId, $"{path}.fingerprintId");
                break;
            case LocatorStrategy.Fingerprint:
                Reject(expression.Selector, $"{path}.selector");
                Reject(expression.Role, $"{path}.role");
                Reject(expression.Name, $"{path}.name");
                Reject(expression.Text, $"{path}.text");
                if (expression.Exact is not null || expression.HasText is not null)
                {
                    throw new InvalidOperationException(
                        $"{path} fingerprint não aceita exact nem hasText.");
                }

                break;
        }
    }

    private static void Reject(string? value, string path)
    {
        if (value is not null)
        {
            throw new InvalidOperationException(
                $"{path} não é compatível com a estratégia escolhida.");
        }
    }

    private static void ValidateFingerprints(LocatorDefinition locator)
    {
        if (locator.Fingerprints.Count > MaximumFingerprintsPerLocator)
        {
            throw new InvalidOperationException(
                $"O locator '{locator.Id}' excedeu {MaximumFingerprintsPerLocator} fingerprints.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fingerprint in locator.Fingerprints)
        {
            RequireText(fingerprint.Id, $"locators.{locator.Id}.fingerprint.id");
            RequireText(
                fingerprint.TagName,
                $"locators.{locator.Id}.fingerprint.{fingerprint.Id}.tagName");
            if (!ids.Add(fingerprint.Id))
            {
                throw new InvalidOperationException(
                    $"O fingerprint ID '{fingerprint.Id}' está duplicado em '{locator.Id}'.");
            }

            ValidateFingerprintText(fingerprint.Text, locator.Id, fingerprint.Id);
            ValidateAttributes(fingerprint.Attributes, locator.Id, fingerprint.Id);
            if (fingerprint.Ancestors.Count > MaximumFingerprintAncestors)
            {
                throw new InvalidOperationException(
                    $"O fingerprint '{fingerprint.Id}' em '{locator.Id}' excede " +
                    $"{MaximumFingerprintAncestors} ancestrais.");
            }

            if (fingerprint.PreviousSiblings.Count > MaximumFingerprintSiblingsPerSide ||
                fingerprint.NextSiblings.Count > MaximumFingerprintSiblingsPerSide)
            {
                throw new InvalidOperationException(
                    $"O fingerprint '{fingerprint.Id}' em '{locator.Id}' excede " +
                    $"{MaximumFingerprintSiblingsPerSide} irmãos por lado.");
            }
            foreach (var node in fingerprint.Ancestors
                         .Concat(fingerprint.PreviousSiblings)
                         .Concat(fingerprint.NextSiblings))
            {
                RequireText(node.TagName, "fingerprint.node.tagName");
                ValidateFingerprintText(node.Text, locator.Id, fingerprint.Id);
                ValidateAttributes(node.Attributes, locator.Id, fingerprint.Id);
            }
        }
    }

    private static void ValidateFingerprintText(string? text, string locatorId, string id)
    {
        if (text?.Length > MaximumFingerprintTextLength)
        {
            throw new InvalidOperationException(
                $"O texto do fingerprint '{id}' em '{locatorId}' excede o limite.");
        }
    }

    private static void ValidateAttributes(
        IReadOnlyDictionary<string, string> attributes,
        string locatorId,
        string fingerprintId)
    {
        if (attributes.Count > MaximumFingerprintAttributes)
        {
            throw new InvalidOperationException(
                $"O fingerprint '{fingerprintId}' em '{locatorId}' possui atributos demais.");
        }

        var sensitive = attributes.Keys.FirstOrDefault(SensitiveAttributeNames.Contains);
        if (sensitive is not null)
        {
            throw new InvalidOperationException(
                $"O atributo sensível '{sensitive}' não pode entrar em fingerprint.");
        }
    }

    private static void RequireText(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{path} é obrigatório.");
        }
    }
}
