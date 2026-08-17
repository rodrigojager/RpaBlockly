using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RpaFlow.Playwright;

internal sealed class FormActionHandler : IFlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fill",
            "selectOption",
            "setChecked",
            "pressKey",
            "typeSequentially",
            "typeAcrossInputs",
            "upload",
            "preserveOrFill",
            "select2",
            "fillMaskedCurrency"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "fill":
                await FillAsync(action, execution, cancellationToken);
                break;
            case "selectoption":
                await SelectOptionAsync(action, execution, cancellationToken);
                break;
            case "setchecked":
                await SetCheckedAsync(action, execution, cancellationToken);
                break;
            case "presskey":
                await PressKeyAsync(action, execution, cancellationToken);
                break;
            case "typesequentially":
                await TypeSequentiallyAsync(action, execution, cancellationToken);
                break;
            case "typeacrossinputs":
                await TypeAcrossInputsAsync(action, execution, cancellationToken);
                break;
            case "upload":
                await UploadAsync(action, execution, cancellationToken);
                break;
            case "preserveorfill":
                await PreserveOrFillAsync(action, execution, cancellationToken);
                break;
            case "select2":
                await Select2Async(action, execution, cancellationToken);
                break;
            case "fillmaskedcurrency":
                await FillMaskedCurrencyAsync(action, execution, cancellationToken);
                break;
            default:
                throw new InvalidOperationException(
                    $"O handler de formulário não interpreta '{action.Type}'.");
        }
    }

    private static async Task FillAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var value = LegacyFlowValueResolver.ResolveRequired(
            action,
            execution.Context.Data);
        await execution.CreateLocator(action)
            .FillWhenReadyAsync(value, action.Name, cancellationToken);
    }

    private static async Task SelectOptionAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var value = LegacyFlowValueResolver.ResolveRequired(
            action,
            execution.Context.Data);
        var locator = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        cancellationToken.ThrowIfCancellationRequested();

        var selected = action.OptionMode?.ToLowerInvariant() switch
        {
            "value" => await locator.SelectOptionAsync(
                new SelectOptionValue { Value = value }),
            "label" => await locator.SelectOptionAsync(
                new SelectOptionValue { Label = value }),
            "index" when int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var index) && index >= 0 => await locator.SelectOptionAsync(
                    new SelectOptionValue { Index = index }),
            "index" => throw new InvalidOperationException(
                $"A ação '{action.Name}' exige um índice inteiro maior ou igual a zero."),
            _ => throw new InvalidOperationException(
                $"Modo de seleção inválido em '{action.Name}': '{action.OptionMode}'.")
        };

        if (selected.Count == 0)
        {
            throw new InvalidOperationException(
                $"Nenhuma opção foi selecionada em '{action.Name}'.");
        }
    }

    private static async Task SetCheckedAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var value = LegacyFlowValueResolver.ResolveRequired(
            action,
            execution.Context.Data);
        if (!bool.TryParse(value, out var expected))
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' exige um valor booleano; recebido '{value}'.");
        }

        var locator = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        cancellationToken.ThrowIfCancellationRequested();
        await locator.SetCheckedAsync(expected);
        if (await locator.IsCheckedAsync() != expected)
        {
            throw new InvalidOperationException(
                $"O estado marcado de '{action.Name}' não permaneceu como esperado.");
        }
    }

    private static async Task PressKeyAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var key = LegacyFlowValueResolver.ResolveRequired(
            action,
            execution.Context.Data);
        var locator = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        cancellationToken.ThrowIfCancellationRequested();
        await locator.PressAsync(key);
    }

    private static async Task TypeSequentiallyAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var value = LegacyFlowValueResolver.ResolveRequired(action, context.Data);
        var locator = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        cancellationToken.ThrowIfCancellationRequested();

        if (action.ClearFirst)
        {
            await locator.ClickAsync();
            await locator.PressAsync("Control+A");
            await locator.PressAsync("Backspace");

            locator = execution.CreateLocator(action);
            await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        }

        await locator.ClickAsync();
        await locator.PressSequentiallyAsync(
            value,
            new LocatorPressSequentiallyOptions { Delay = action.DelayMs ?? 50 });

        if (action.BlurAfter)
        {
            await locator.PressAsync("Tab");
            await context.Readiness.WaitForPageToSettleAsync(cancellationToken);
        }

        locator = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        var actual = await locator.InputValueAsync();
        if (!string.Equals(actual, value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A digitação sequencial de '{action.Name}' não permaneceu no campo.");
        }
    }

    private static async Task TypeAcrossInputsAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var value = LegacyFlowValueResolver.ResolveRequired(action, context.Data);
        var textElements = GetTextElements(value);
        _ = await ResolveVisibleInputsAsync(
            action,
            execution,
            textElements.Length,
            cancellationToken);

        if (action.ClearFirst)
        {
            for (var index = 0; index < textElements.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var inputs = await ResolveVisibleInputsAsync(
                    action,
                    execution,
                    textElements.Length,
                    cancellationToken);
                var input = inputs[index];
                await input.ClickAsync();
                await input.PressAsync("Control+A");
                await input.PressAsync("Backspace");
            }
        }

        var delay = action.DelayMs ?? 50;
        for (var index = 0; index < textElements.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = await ResolveVisibleInputsAsync(
                action,
                execution,
                textElements.Length,
                cancellationToken);
            var input = inputs[index];
            await input.ClickAsync();
            await input.PressSequentiallyAsync(
                textElements[index],
                new LocatorPressSequentiallyOptions { Delay = delay });

            if (delay > 0 && index < textElements.Length - 1)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        if (action.BlurAfter && textElements.Length > 0)
        {
            var inputs = await ResolveVisibleInputsAsync(
                action,
                execution,
                textElements.Length,
                cancellationToken);
            await inputs[^1].PressAsync("Tab");
            await context.Readiness.WaitForPageToSettleAsync(cancellationToken);
        }

        var finalInputs = await ResolveVisibleInputsAsync(
            action,
            execution,
            textElements.Length,
            cancellationToken);
        var actualElements = new string[finalInputs.Count];
        for (var index = 0; index < finalInputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            actualElements[index] = await finalInputs[index].InputValueAsync();
        }

        if (!string.Equals(
                string.Concat(actualElements),
                value,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A digitação segmentada de '{action.Name}' não permaneceu nos campos.");
        }
    }

    private static string[] GetTextElements(string value)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        return elements.ToArray();
    }

    private static async Task<IReadOnlyList<ILocator>> ResolveVisibleInputsAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locator = execution.CreateLocator(action);
        var candidateCount = await locator.CountAsync();
        var inputs = new List<ILocator>(candidateCount);
        for (var index = 0; index < candidateCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = locator.Nth(index);
            if (await candidate.IsVisibleAsync())
            {
                inputs.Add(candidate);
            }
        }

        if (inputs.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' exige {expectedCount} inputs visíveis, " +
                $"mas o seletor encontrou {inputs.Count}.");
        }

        for (var index = 0; index < inputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elementName = await inputs[index]
                .EvaluateAsync<string>("element => element.localName");
            if (!elementName.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"A ação '{action.Name}' exige inputs, mas o elemento visível " +
                    $"de índice {index} é '{elementName}'.");
            }
        }

        return inputs;
    }

    private static async Task UploadAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var filePath = LegacyFlowValueResolver.ResolveOptional(action, context.Data);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            if (action.Optional)
            {
                Console.WriteLine($"  Anexo opcional não informado: {action.Name}.");
                return;
            }

            throw new InvalidOperationException(
                $"O arquivo obrigatório da ação '{action.Name}' não foi informado.");
        }

        var resolvedPath = Path.GetFullPath(
            Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(context.Options.ConfigurationDirectory, filePath));
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"Arquivo da ação '{action.Name}' não encontrado.",
                resolvedPath);
        }

        await context.Readiness.UploadAndWaitAsync(
            execution.CreateLocator(action),
            resolvedPath,
            action.Name,
            cancellationToken);
        Console.WriteLine($"  Anexo processado: {action.Name}.");
    }

    private static async Task PreserveOrFillAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var locator = execution.CreateLocator(action);
        var expected = LegacyFlowValueResolver.ResolveRequired(action, context.Data);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        var current = await locator.InputValueAsync();

        if (string.IsNullOrWhiteSpace(current))
        {
            await locator.FillWhenReadyAsync(expected, action.Name, cancellationToken);
            return;
        }

        var matches = action.Comparison?.ToLowerInvariant() switch
        {
            "exact" => string.Equals(current, expected, StringComparison.Ordinal),
            "caseinsensitive" => string.Equals(
                current,
                expected,
                StringComparison.OrdinalIgnoreCase),
            "currency" => TryParseNumber(current, out var currentAmount) &&
                TryParseNumber(expected, out var expectedAmount) &&
                currentAmount == expectedAmount,
            _ => false
        };

        if (!matches)
        {
            throw new InvalidOperationException(
                $"A página preencheu '{action.Name}' com '{current}', " +
                $"valor diferente do esperado '{expected}'.");
        }

        Console.WriteLine(
            $"  Valor preservado porque já foi preenchido: {action.Name}.");
    }

    private static async Task Select2Async(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var expected = LegacyFlowValueResolver.ResolveRequired(action, context.Data);
        var nativeSelect = execution.CreateLocator(action);
        await LocatorActions.EnsureSingleAttachedAsync(nativeSelect, action.Name);
        var currentValue = await nativeSelect.InputValueAsync();

        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            var currentLabel =
                await nativeSelect.Locator("option:checked").TextContentAsync() ?? currentValue;
            if (!SelectValuesAreEqual(
                    currentLabel,
                    expected,
                    action.Comparison,
                    legacyExistingValue: true))
            {
                throw new InvalidOperationException(
                    $"A página preencheu '{action.Name}' com '{currentLabel.Trim()}', " +
                    $"valor diferente do esperado '{expected}'.");
            }

            Console.WriteLine(
                $"  Valor preservado porque já foi preenchido: {action.Name}.");
            return;
        }

        await nativeSelect.ScrollIntoViewIfNeededAsync();
        var trigger = FlowLocatorFactory.Create(
            context.Page,
            action.TriggerSelector!,
            scope: null,
            hasText: null,
            frameSelectors: action.FrameSelectors);
        await trigger.ClickWhenReadyAsync(action.Name, cancellationToken);
        await context.Readiness.WaitForPageToSettleAsync(cancellationToken);

        var options = FlowLocatorFactory.Create(
            context.Page,
            action.OptionSelector!,
            scope: null,
            hasText: null,
            frameSelectors: action.FrameSelectors);
        await options.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = action.TimeoutMs
        });
        var labels = (await options.AllTextContentsAsync())
            .Select(label => label.Trim())
            .ToArray();
        int selectedIndex;
        if (string.IsNullOrWhiteSpace(action.Comparison))
        {
            selectedIndex = Array.FindIndex(labels, label =>
                string.Equals(label, expected, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex < 0)
            {
                selectedIndex = Array.FindIndex(labels, label =>
                    RatesAreEqual(label, expected));
            }
        }
        else
        {
            selectedIndex = Array.FindIndex(labels, label =>
                SelectValuesAreEqual(
                    label,
                    expected,
                    action.Comparison,
                    legacyExistingValue: false));
        }

        if (selectedIndex < 0)
        {
            await context.Page.Keyboard.PressAsync("Escape");
            throw new InvalidOperationException(
                $"A opção '{expected}' não foi encontrada em '{action.Name}'. " +
                $"Opções exibidas: {string.Join(", ", labels.Select(label => $"'{label}'"))}");
        }

        await options.Nth(selectedIndex).ClickAsync();
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(
            action.TimeoutMs ?? context.Options.ActionTimeoutSeconds * 1_000);
        while (string.IsNullOrWhiteSpace(await nativeSelect.InputValueAsync()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= timeoutAt)
            {
                throw new TimeoutException(
                    $"O portal não confirmou a seleção em '{action.Name}'.");
            }

            await Task.Delay(50, cancellationToken);
        }
        Console.WriteLine($"  Opção selecionada: {labels[selectedIndex]}.");
    }

    private static async Task FillMaskedCurrencyAsync(
        FlowActionDefinition action,
        FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var locator = execution.CreateLocator(action);
        var expected = LegacyFlowValueResolver.ResolveRequired(action, context.Data);
        await LocatorActions.EnsureSingleVisibleAsync(locator, action.Name);
        var current = await locator.InputValueAsync();

        if (!string.IsNullOrWhiteSpace(current))
        {
            if (!TryParseNumber(current, out var currentAmount) ||
                !TryParseNumber(expected, out var expectedAmount) ||
                currentAmount != expectedAmount)
            {
                throw new InvalidOperationException(
                    $"A página preencheu '{action.Name}' com '{current}', " +
                    $"valor diferente do esperado '{expected}'.");
            }

            Console.WriteLine(
                $"  Valor preservado porque já foi preenchido: {action.Name}.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParseNumber(expected, out var amountToType))
        {
            throw new InvalidOperationException(
                $"Valor monetário inválido em '{action.Name}': '{expected}'.");
        }

        var decimalPlaces = action.DecimalPlaces ?? 2;
        var scale = 1m;
        for (var index = 0; index < decimalPlaces; index++)
        {
            scale *= 10m;
        }

        var minorUnits = decimal.Round(
            amountToType * scale,
            0,
            MidpointRounding.AwayFromZero);
        var digits = minorUnits.ToString("0", CultureInfo.InvariantCulture);
        await locator.ClickAsync();
        await locator.PressAsync("Control+A");
        await locator.PressAsync("Backspace");
        await locator.PressSequentiallyAsync(
            digits,
            new LocatorPressSequentiallyOptions { Delay = action.DelayMs ?? 30 });
        await locator.PressAsync(action.CommitKey ?? "Tab");

        var actual = await locator.InputValueAsync();
        if (!TryParseNumber(actual, out var actualAmount) || actualAmount != amountToType)
        {
            throw new InvalidOperationException(
                $"A máscara de '{action.Name}' produziu '{actual}', " +
                $"valor diferente do esperado '{expected}'.");
        }
    }

    private static bool RatesAreEqual(string first, string second) =>
        TryParseNumber(first, out var firstRate) &&
        TryParseNumber(second, out var secondRate) &&
        firstRate == secondRate;

    private static bool SelectValuesAreEqual(
        string first,
        string second,
        string? comparison,
        bool legacyExistingValue)
    {
        var firstValue = first.Trim();
        var secondValue = second.Trim();
        return comparison?.ToLowerInvariant() switch
        {
            "exact" => string.Equals(
                firstValue,
                secondValue,
                StringComparison.Ordinal),
            "caseinsensitive" => string.Equals(
                firstValue,
                secondValue,
                StringComparison.OrdinalIgnoreCase),
            "numeric" => RatesAreEqual(firstValue, secondValue),
            null or "" when legacyExistingValue =>
                RatesAreEqual(firstValue, secondValue),
            null or "" => string.Equals(
                    firstValue,
                    secondValue,
                    StringComparison.OrdinalIgnoreCase) ||
                RatesAreEqual(firstValue, secondValue),
            _ => false
        };
    }

    private static bool TryParseNumber(string value, out decimal number)
    {
        var cleanValue = Regex.Replace(value, "[^0-9,.-]+", string.Empty).Trim();
        var culture = cleanValue.Contains(',')
            ? CultureInfo.GetCultureInfo("pt-BR")
            : CultureInfo.InvariantCulture;
        return decimal.TryParse(cleanValue, NumberStyles.Number, culture, out number);
    }
}
