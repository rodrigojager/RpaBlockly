using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using RpaFlow.Contracts.V2;
using RpaFlow.Runtime.V2;
using FlowActionDefinition = RpaFlow.Contracts.V2.FlowActionDefinition;

namespace RpaFlow.Playwright.V2;

internal sealed class V2FormActionHandler : IV2FlowActionHandler
{
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fill", "selectOption", "setChecked", "pressKey", "typeSequentially",
            "typeAcrossInputs", "upload", "preserveOrFill", "select2",
            "fillMaskedCurrency"
        };

    public async Task ExecuteAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "fill":
                await FillAsync(action, execution, cancellationToken);
                return;
            case "selectoption":
                await SelectOptionAsync(action, execution, cancellationToken);
                return;
            case "setchecked":
                await SetCheckedAsync(action, execution, cancellationToken);
                return;
            case "presskey":
                await PressKeyAsync(action, execution, cancellationToken);
                return;
            case "typesequentially":
                await TypeSequentiallyAsync(action, execution, cancellationToken);
                return;
            case "typeacrossinputs":
                await TypeAcrossInputsAsync(action, execution, cancellationToken);
                return;
            case "upload":
                await UploadAsync(action, execution, cancellationToken);
                return;
            case "preserveorfill":
                await PreserveOrFillAsync(action, execution, cancellationToken);
                return;
            case "select2":
                await Select2Async(action, execution, cancellationToken);
                return;
            case "fillmaskedcurrency":
                await FillMaskedCurrencyAsync(action, execution, cancellationToken);
                return;
            default:
                throw new InvalidOperationException(
                    $"O handler V2 de formulário não interpreta '{action.Type}'.");
        }
    }

    private static async Task FillAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var target = await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken);
        await target.Locator.FillAsync(
            V2FlowValueResolver.ResolveRequired(action, execution.Context.Data));
    }

    private static async Task SelectOptionAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var value = V2FlowValueResolver.ResolveRequired(action, execution.Context.Data);
        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken)).Locator;
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
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var raw = V2FlowValueResolver.ResolveRequired(action, execution.Context.Data);
        if (!bool.TryParse(raw, out var expected))
        {
            throw new InvalidOperationException(
                $"A ação '{action.Name}' exige um valor booleano; recebido '{raw}'.");
        }

        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken)).Locator;
        await locator.SetCheckedAsync(expected);
        if (await locator.IsCheckedAsync() != expected)
        {
            throw new InvalidOperationException(
                $"O estado marcado de '{action.Name}' não permaneceu como esperado.");
        }
    }

    private static async Task PressKeyAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken)).Locator;
        await locator.PressAsync(
            V2FlowValueResolver.ResolveRequired(action, execution.Context.Data));
    }

    private static async Task TypeSequentiallyAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var value = V2FlowValueResolver.ResolveRequired(action, execution.Context.Data);
        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken)).Locator;
        if (action.ClearFirst)
        {
            await locator.ClickAsync();
            await locator.PressAsync("Control+A");
            await locator.PressAsync("Backspace");
            locator = (await execution.ResolveTargetAsync(
                action,
                LocatorRequiredState.Visible,
                cancellationToken)).Locator;
        }

        await locator.ClickAsync();
        await locator.PressSequentiallyAsync(
            value,
            new LocatorPressSequentiallyOptions { Delay = action.DelayMs ?? 50 });
        if (action.BlurAfter)
        {
            await locator.PressAsync("Tab");
            await execution.Context.Readiness.WaitForPageToSettleAsync(cancellationToken);
        }

        locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken)).Locator;
        if (!string.Equals(await locator.InputValueAsync(), value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A digitação sequencial de '{action.Name}' não permaneceu no campo.");
        }
    }

    private static async Task TypeAcrossInputsAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var value = V2FlowValueResolver.ResolveRequired(action, execution.Context.Data);
        var elements = GetTextElements(value);
        _ = await ResolveVisibleInputsAsync(
            action,
            execution,
            elements.Length,
            cancellationToken);
        if (action.ClearFirst)
        {
            for (var index = 0; index < elements.Length; index++)
            {
                var inputs = await ResolveVisibleInputsAsync(
                    action,
                    execution,
                    elements.Length,
                    cancellationToken);
                await inputs[index].ClickAsync();
                await inputs[index].PressAsync("Control+A");
                await inputs[index].PressAsync("Backspace");
            }
        }

        var delay = action.DelayMs ?? 50;
        for (var index = 0; index < elements.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = await ResolveVisibleInputsAsync(
                action,
                execution,
                elements.Length,
                cancellationToken);
            await inputs[index].ClickAsync();
            await inputs[index].PressSequentiallyAsync(
                elements[index],
                new LocatorPressSequentiallyOptions { Delay = delay });
            if (delay > 0 && index < elements.Length - 1)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        if (action.BlurAfter && elements.Length > 0)
        {
            var inputs = await ResolveVisibleInputsAsync(
                action,
                execution,
                elements.Length,
                cancellationToken);
            await inputs[^1].PressAsync("Tab");
            await execution.Context.Readiness.WaitForPageToSettleAsync(cancellationToken);
        }

        var finalInputs = await ResolveVisibleInputsAsync(
            action,
            execution,
            elements.Length,
            cancellationToken);
        var actual = new string[finalInputs.Count];
        for (var index = 0; index < finalInputs.Count; index++)
        {
            actual[index] = await finalInputs[index].InputValueAsync();
        }

        if (!string.Equals(string.Concat(actual), value, StringComparison.Ordinal))
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
        V2FlowActionExecutionScope execution,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Attached,
            cancellationToken)).Locator;
        var candidateCount = await locator.CountAsync();
        var inputs = new List<ILocator>(candidateCount);
        for (var index = 0; index < candidateCount; index++)
        {
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
                $"mas o locator encontrou {inputs.Count}.");
        }

        for (var index = 0; index < inputs.Count; index++)
        {
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
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var context = execution.Context;
        var filePath = V2FlowValueResolver.ResolveOptional(action, context.Data);
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

        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Attached,
            cancellationToken)).Locator;
        await context.Readiness.UploadAndWaitAsync(
            locator,
            resolvedPath,
            action.Name,
            cancellationToken);
        Console.WriteLine($"  Anexo processado: {action.Name}.");
    }

    private static async Task PreserveOrFillAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken)).Locator;
        var expected = V2FlowValueResolver.ResolveRequired(action, execution.Context.Data);
        var current = await locator.InputValueAsync();
        if (string.IsNullOrWhiteSpace(current))
        {
            await locator.FillAsync(expected);
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

        Console.WriteLine($"  Valor preservado porque já foi preenchido: {action.Name}.");
    }

    private static async Task Select2Async(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var expected = V2FlowValueResolver.ResolveRequired(action, execution.Context.Data);
        var nativeSelect = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Attached,
            cancellationToken)).Locator;
        var currentValue = await nativeSelect.InputValueAsync();
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            var currentLabel = await nativeSelect.EvaluateAsync<string>(
                "select => select.selectedOptions[0]?.textContent ?? select.value");
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

            Console.WriteLine($"  Valor preservado porque já foi preenchido: {action.Name}.");
            return;
        }

        await nativeSelect.ScrollIntoViewIfNeededAsync();
        var trigger = await execution.ResolveAsync(
            action.Trigger ?? throw new InvalidOperationException(
                $"A ação '{action.Name}' exige o locator trigger."),
            LocatorRequiredState.Visible,
            cancellationToken,
            action.TimeoutMs);
        await trigger.Locator.ClickAsync();
        await execution.Context.Readiness.WaitForPageToSettleAsync(cancellationToken);
        var options = await execution.ResolveAsync(
            action.Options ?? throw new InvalidOperationException(
                $"A ação '{action.Name}' exige o locator options."),
            LocatorRequiredState.Visible,
            cancellationToken,
            action.TimeoutMs);
        var labels = (await options.Locator.AllTextContentsAsync())
            .Select(label => label.Trim())
            .ToArray();
        var selectedIndex = string.IsNullOrWhiteSpace(action.Comparison)
            ? FindDefault(labels, expected)
            : Array.FindIndex(labels, label =>
                SelectValuesAreEqual(
                    label,
                    expected,
                    action.Comparison,
                    legacyExistingValue: false));
        if (selectedIndex < 0)
        {
            await execution.Context.Page.Keyboard.PressAsync("Escape");
            throw new InvalidOperationException(
                $"A opção '{expected}' não foi encontrada em '{action.Name}'. " +
                $"Opções exibidas: {string.Join(", ", labels.Select(label => $"'{label}'"))}");
        }

        await options.Locator.Nth(selectedIndex).ClickAsync();
        var timeoutAt = DateTime.UtcNow.AddMilliseconds(
            action.TimeoutMs ?? execution.Context.Options.ActionTimeoutSeconds * 1_000);
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

    private static int FindDefault(string[] labels, string expected)
    {
        var index = Array.FindIndex(labels, label =>
            string.Equals(label, expected, StringComparison.OrdinalIgnoreCase));
        return index >= 0
            ? index
            : Array.FindIndex(labels, label => RatesAreEqual(label, expected));
    }

    private static async Task FillMaskedCurrencyAsync(
        FlowActionDefinition action,
        V2FlowActionExecutionScope execution,
        CancellationToken cancellationToken)
    {
        var locator = (await execution.ResolveTargetAsync(
            action,
            LocatorRequiredState.Visible,
            cancellationToken)).Locator;
        var expected = V2FlowValueResolver.ResolveRequired(action, execution.Context.Data);
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

            Console.WriteLine($"  Valor preservado porque já foi preenchido: {action.Name}.");
            return;
        }

        if (!TryParseNumber(expected, out var amountToType))
        {
            throw new InvalidOperationException(
                $"Valor monetário inválido em '{action.Name}': '{expected}'.");
        }

        var scale = 1m;
        for (var index = 0; index < (action.DecimalPlaces ?? 2); index++)
        {
            scale *= 10m;
        }

        var digits = decimal.Round(
            amountToType * scale,
            0,
            MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
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
            "exact" => string.Equals(firstValue, secondValue, StringComparison.Ordinal),
            "caseinsensitive" => string.Equals(
                firstValue,
                secondValue,
                StringComparison.OrdinalIgnoreCase),
            "numeric" => RatesAreEqual(firstValue, secondValue),
            null or "" when legacyExistingValue => RatesAreEqual(firstValue, secondValue),
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
