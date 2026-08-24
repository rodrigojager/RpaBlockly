using Microsoft.Playwright;
using System.Text;
using System.Text.Json;
using RpaFlow.Playwright.V2;

namespace RpaFlow.Playwright;

public sealed class ExecutionArtifacts
{
    private const int MaximumDiagnosticHtmlCharacters = 500_000;
    private readonly string _outputDirectory;
    private readonly string _executionDirectoryName;
    private readonly long _maximumArtifactBytes;
    private readonly int _maximumFiles;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private int _savedFiles;
    private IPage _page;

    public ExecutionArtifacts(
        IPage page,
        string outputDirectory,
        string? executionId = null,
        long maximumArtifactBytes = 50 * 1024 * 1024,
        int maximumFiles = 100,
        TimeSpan? retention = null)
    {
        if (maximumArtifactBytes < 1 || maximumFiles < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumArtifactBytes),
                "Os limites de artefato devem ser positivos.");
        }

        _page = page;
        _outputDirectory = Path.GetFullPath(outputDirectory);
        _maximumArtifactBytes = maximumArtifactBytes;
        _maximumFiles = maximumFiles;
        var safeExecutionId = SanitizeFileName(
            executionId,
            Guid.NewGuid().ToString("N"));
        _executionDirectoryName =
            $"{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}-" +
            safeExecutionId[..Math.Min(safeExecutionId.Length, 80)];
        CleanupExpiredExecutionDirectories(retention ?? TimeSpan.FromDays(30));
    }

    public Task<string> CaptureScreenshotAsync(
        string label,
        ArtifactDestination? destination = null)
    {
        destination ??= new ArtifactDestination();
        var requestedName = destination.FileName ?? label;
        var extension = Path.GetExtension(requestedName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            requestedName += ".png";
        }
        else if (extension is not (".png" or ".jpg" or ".jpeg") &&
            extension is not (".PNG" or ".JPG" or ".JPEG"))
        {
            throw new InvalidOperationException(
                $"A screenshot '{requestedName}' deve usar extensão .png, .jpg ou .jpeg.");
        }

        return SaveAsync(
            requestedName,
            destination,
            path => _page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = path,
                FullPage = true
            }));
    }

    public Task<string> CaptureElementScreenshotAsync(
        ILocator locator,
        string label,
        ArtifactDestination? destination = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        destination ??= new ArtifactDestination();
        var requestedName = destination.FileName ?? label;
        if (string.IsNullOrWhiteSpace(Path.GetExtension(requestedName)))
        {
            requestedName += ".png";
        }

        return SaveAsync(
            requestedName,
            destination,
            path => locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path }));
    }

    public async Task<string> CaptureSanitizedScreenshotAsync(string label)
    {
        try
        {
            await AddDiagnosticMaskAsync();
            return await CaptureScreenshotAsync(label);
        }
        finally
        {
            await RemoveDiagnosticMaskAsync();
        }
    }

    public Task<string> SaveDownloadAsync(
        IDownload download,
        ArtifactDestination? destination = null) =>
        SaveAsync(
            destination?.FileName ?? download.SuggestedFilename,
            destination ?? new ArtifactDestination(),
            download.SaveAsAsync);

    public Task<string> SaveBytesAsync(
        byte[] contents,
        string suggestedFileName,
        ArtifactDestination? destination = null,
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            destination?.FileName ?? suggestedFileName,
            destination ?? new ArtifactDestination(),
            path => File.WriteAllBytesAsync(path, contents, cancellationToken));

    public void SwitchPage(IPage page) => _page = page;

    public async Task<FailureDiagnosticArtifacts> CaptureFailureDiagnosticsAsync(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string? screenshotPath = null;
        string? htmlPath = null;
        string? reportPath = null;
        try
        {
            screenshotPath = await CaptureSanitizedScreenshotAsync("falha");
        }
        catch
        {
            // O DOM pode não estar mais disponível; os demais diagnósticos continuam.
        }

        try
        {
            var html = await CaptureSanitizedHtmlAsync();
            htmlPath = await SaveTextAsync("falha.html", html);
        }
        catch
        {
            // O relatório ainda pode ser produzido quando o DOM já foi encerrado.
        }

        try
        {
            var root = FindLocatorResolutionException(exception);
            var report = new
            {
                version = 1,
                failureType = exception.GetType().Name,
                locator = root is null
                    ? null
                    : new
                    {
                        id = root.LocatorId,
                        attempts = root.Attempts.Select(attempt => new
                        {
                            candidateId = attempt.CandidateId,
                            candidateIndex = attempt.CandidateIndex,
                            attempt.Succeeded,
                            reason = attempt.FailureReason?.ToString(),
                            attempt.MatchCount,
                            attempt.ElapsedMilliseconds,
                            attempt.Detail
                        })
                    }
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }).ReplaceLineEndings("\n") + "\n";
            reportPath = await SaveTextAsync("resolucao.json", json);
        }
        catch
        {
            // Artefatos auxiliares nunca ocultam a falha original.
        }

        return new FailureDiagnosticArtifacts(screenshotPath, htmlPath, reportPath);
    }

    private async Task<string> SaveAsync(
        string requestedFileName,
        ArtifactDestination destination,
        Func<string, Task> save)
    {
        await _fileLock.WaitAsync();
        try
        {
            if (_savedFiles >= _maximumFiles)
            {
                throw new InvalidOperationException(
                    $"A execução excedeu o limite de {_maximumFiles} artefatos.");
            }

            var directory = ResolveDirectory(destination);
            Directory.CreateDirectory(directory);
            var fileName = SanitizeFileName(requestedFileName, "arquivo");
            var reservation = ReserveDestination(
                Path.Combine(directory, fileName),
                destination.ConflictStrategy);
            var temporaryPath = CreateTemporaryPath(directory, fileName);
            try
            {
                await save(temporaryPath);
                var length = new FileInfo(temporaryPath).Length;
                if (length > _maximumArtifactBytes)
                {
                    throw new InvalidOperationException(
                        $"O artefato excedeu o limite de {_maximumArtifactBytes} bytes.");
                }

                File.Move(temporaryPath, reservation.Path, overwrite: true);
                _savedFiles++;
                return reservation.Path;
            }
            catch
            {
                TryDelete(temporaryPath);
                if (reservation.Reserved)
                {
                    TryDelete(reservation.Path);
                }
                throw;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void CleanupExpiredExecutionDirectories(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero || !Directory.Exists(_outputDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - retention;
        foreach (var directory in Directory.EnumerateDirectories(_outputDirectory))
        {
            try
            {
                var name = Path.GetFileName(directory);
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        name,
                        "^[0-9]{8}-[0-9]{9}-",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
                    Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(directory);
                if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relative = Path.GetRelativePath(_outputDirectory, fullPath);
                if (relative == ".." ||
                    relative.StartsWith(
                        ".." + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Directory.Delete(fullPath, recursive: true);
            }
            catch
            {
                // Retenção é best effort e não pode impedir a execução atual.
            }
        }
    }

    private Task<string> SaveTextAsync(string name, string contents) =>
        SaveAsync(
            name,
            new ArtifactDestination(),
            path => File.WriteAllTextAsync(path, contents, new UTF8Encoding(false, true)));

    private async Task AddDiagnosticMaskAsync()
    {
        foreach (var frame in _page.Frames)
        {
            try
            {
                await frame.EvaluateAsync(
                    """
                    () => {
                      const id = "rpa-diagnostic-mask";
                      const css = `
                        input, textarea, select, [contenteditable="true"],
                        [data-private], [data-sensitive] {
                          color: transparent !important;
                          text-shadow: none !important;
                          background: #111827 !important;
                          caret-color: transparent !important;
                        }`;
                      const install = root => {
                        root.querySelector(`#${id}`)?.remove();
                        const style = document.createElement("style");
                        style.id = id;
                        style.textContent = css;
                        root.append(style);
                        root.querySelectorAll("*").forEach(node => {
                          if (node.shadowRoot) install(node.shadowRoot);
                        });
                      };
                      install(document.documentElement);
                    }
                    """);
            }
            catch
            {
                // Um frame pode navegar ou desaparecer enquanto os demais são protegidos.
            }
        }
    }

    private async Task RemoveDiagnosticMaskAsync()
    {
        foreach (var frame in _page.Frames)
        {
            try
            {
                await frame.EvaluateAsync(
                    """
                    () => {
                      const remove = root => {
                        root.querySelector("#rpa-diagnostic-mask")?.remove();
                        root.querySelectorAll("*").forEach(node => {
                          if (node.shadowRoot) remove(node.shadowRoot);
                        });
                      };
                      remove(document.documentElement);
                    }
                    """);
            }
            catch
            {
                // A página ou o frame pode ter encerrado durante a captura.
            }
        }
    }

    private async Task<string> CaptureSanitizedHtmlAsync()
    {
        var html = await _page.EvaluateAsync<string>(
            """
            () => {
              const clone = document.documentElement.cloneNode(true);
              clone.querySelectorAll("script, style, link[rel='stylesheet'], iframe")
                .forEach(node => node.remove());
              clone.querySelectorAll(
                "input, textarea, select, [contenteditable='true'], [data-private], [data-sensitive]")
                .forEach(node => {
                  node.textContent = "[CONTEÚDO REDIGIDO]";
                  node.setAttribute("data-rpa-redacted", "true");
                });
              clone.querySelectorAll("*").forEach(node => {
                for (const attribute of [...node.attributes]) {
                  const name = attribute.name.toLowerCase();
                  if (name.startsWith("on") ||
                      /value|token|secret|password|cookie|authorization|api[-_]?key|srcdoc/.test(name)) {
                    node.removeAttribute(attribute.name);
                  } else if (name === "href" || name === "src") {
                    const value = attribute.value.split(/[?#]/, 1)[0];
                    node.setAttribute(attribute.name, value);
                  }
                }
              });
              return "<!doctype html>\n" + clone.outerHTML;
            }
            """);
        return html.Length <= MaximumDiagnosticHtmlCharacters
            ? html
            : html[..MaximumDiagnosticHtmlCharacters] +
              "\n<!-- conteúdo truncado por segurança -->\n";
    }

    private static LocatorResolutionException? FindLocatorResolutionException(
        Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is LocatorResolutionException resolution)
            {
                return resolution;
            }
        }

        return null;
    }

    private string ResolveDirectory(ArtifactDestination destination)
    {
        var configuredDirectory = destination.Directory;
        string baseDirectory;
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            baseDirectory = _outputDirectory;
        }
        else if (Path.IsPathFullyQualified(configuredDirectory))
        {
            baseDirectory = Path.GetFullPath(configuredDirectory);
        }
        else if (Path.IsPathRooted(configuredDirectory))
        {
            throw new InvalidOperationException(
                "A pasta absoluta de artefatos deve ser totalmente qualificada. " +
                "Use uma unidade completa ou um caminho UNC \\\\servidor\\compartilhamento.");
        }
        else
        {
            baseDirectory = Path.GetFullPath(
                Path.Combine(_outputDirectory, configuredDirectory));
            var relative = Path.GetRelativePath(_outputDirectory, baseDirectory);
            if (relative == ".." ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Uma pasta de artefatos relativa não pode sair de Runtime.OutputDirectory. " +
                    "Use um caminho absoluto para escolher outra raiz.");
            }
        }

        return destination.SeparateByExecution
            ? Path.Combine(baseDirectory, _executionDirectoryName)
            : baseDirectory;
    }

    private static (string Path, bool Reserved) ReserveDestination(
        string requestedPath,
        ArtifactConflictStrategy strategy)
    {
        if (strategy == ArtifactConflictStrategy.Overwrite)
        {
            return (requestedPath, false);
        }

        if (strategy == ArtifactConflictStrategy.Fail)
        {
            if (!TryReserve(requestedPath))
            {
                throw new IOException($"O arquivo de destino já existe: {requestedPath}");
            }

            return (requestedPath, true);
        }

        var directory = Path.GetDirectoryName(requestedPath)!;
        var fileName = Path.GetFileNameWithoutExtension(requestedPath);
        var extension = Path.GetExtension(requestedPath);
        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            var candidate = suffix == 1
                ? requestedPath
                : Path.Combine(directory, $"{fileName} ({suffix}){extension}");
            if (TryReserve(candidate))
            {
                return (candidate, true);
            }
        }

        throw new IOException(
            $"Não foi possível encontrar um nome disponível para: {requestedPath}");
    }

    private static bool TryReserve(string path)
    {
        try
        {
            using var reservation = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    private static string CreateTemporaryPath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(
            directory,
            $".{baseName}-{Guid.NewGuid():N}.tmp{extension}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A exceção original de gravação deve ser preservada.
        }
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        var fileName = Path.GetFileName(value?.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fallback;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(fileName
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}

public sealed record FailureDiagnosticArtifacts(
    string? ScreenshotPath,
    string? SanitizedHtmlPath,
    string? ResolutionReportPath);
