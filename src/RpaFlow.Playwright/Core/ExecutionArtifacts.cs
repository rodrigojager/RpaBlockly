using Microsoft.Playwright;

namespace RpaFlow.Playwright;

public sealed class ExecutionArtifacts
{
    private readonly string _outputDirectory;
    private readonly string _executionDirectoryName;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private IPage _page;

    public ExecutionArtifacts(
        IPage page,
        string outputDirectory,
        string? executionId = null)
    {
        _page = page;
        _outputDirectory = Path.GetFullPath(outputDirectory);
        var safeExecutionId = SanitizeFileName(
            executionId,
            Guid.NewGuid().ToString("N"));
        _executionDirectoryName =
            $"{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}-" +
            safeExecutionId[..Math.Min(safeExecutionId.Length, 80)];
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

    private async Task<string> SaveAsync(
        string requestedFileName,
        ArtifactDestination destination,
        Func<string, Task> save)
    {
        await _fileLock.WaitAsync();
        try
        {
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
                File.Move(temporaryPath, reservation.Path, overwrite: true);
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
