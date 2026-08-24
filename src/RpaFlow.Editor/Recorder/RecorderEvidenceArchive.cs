using System.Text;
using System.Text.Json;
using RpaFlow.Editor.Configuration;

namespace RpaFlow.Editor.Recorder;

internal sealed class RecorderEvidenceArchive
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _root;

    public RecorderEvidenceArchive(EditorPaths paths)
    {
        _root = Path.GetFullPath(Path.Combine(paths.ProjectRoot, ".recorder-imports"));
    }

    public async Task<PreparedArchive> PrepareAsync(
        string revision,
        InspectedRecorderBundle bundle,
        IReadOnlyDictionary<string, string> remappings,
        CancellationToken cancellationToken)
    {
        ValidateSegment(revision, "revisão");
        ValidateSegment(bundle.Manifest.BundleId, "bundleId");
        var directory = Path.GetFullPath(Path.Combine(_root, revision));
        EnsureInsideRoot(directory);
        Directory.CreateDirectory(directory);
        var archivePath = Path.Combine(directory, bundle.Manifest.BundleId + ".rpablockly.zip");
        var mappingPath = Path.Combine(directory, bundle.Manifest.BundleId + ".mapping.json");
        var created = !File.Exists(archivePath);
        if (created)
        {
            try
            {
                await File.WriteAllBytesAsync(archivePath, bundle.ArchiveBytes, cancellationToken);
                var mappingJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    bundleId = bundle.Manifest.BundleId,
                    revision,
                    remappings = remappings.OrderBy(item => item.Key, StringComparer.Ordinal)
                        .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                }, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
                await File.WriteAllTextAsync(mappingPath, mappingJson, Utf8, cancellationToken);
            }
            catch
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
                if (File.Exists(mappingPath)) File.Delete(mappingPath);
                throw;
            }
        }
        else if (!File.ReadAllBytes(archivePath).SequenceEqual(bundle.ArchiveBytes))
        {
            throw new InvalidOperationException(
                "O arquivo de evidências existente diverge do bundle importado.");
        }
        return new PreparedArchive(
            archivePath,
            mappingPath,
            Path.GetRelativePath(Path.GetDirectoryName(_root)!, archivePath),
            created);
    }

    public void Rollback(PreparedArchive prepared)
    {
        if (!prepared.Created) return;
        EnsureInsideRoot(prepared.ArchivePath);
        if (File.Exists(prepared.ArchivePath)) File.Delete(prepared.ArchivePath);
        EnsureInsideRoot(prepared.MappingPath);
        if (File.Exists(prepared.MappingPath)) File.Delete(prepared.MappingPath);
        var directory = Path.GetDirectoryName(prepared.ArchivePath)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static void ValidateSegment(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new InvalidOperationException($"{description} contém caracteres inválidos.");
        }
    }

    private void EnsureInsideRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.Equals(_root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Caminho de evidências escapou da raiz permitida.");
        }
    }
}

internal sealed record PreparedArchive(
    string ArchivePath,
    string MappingPath,
    string RelativeArchivePath,
    bool Created);
