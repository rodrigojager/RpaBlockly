using System.Text;
using System.Text.Json;
using RpaFlow.Editor.Configuration;

namespace RpaFlow.Editor.Recorder;

internal sealed class RecorderImportAudit
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _root;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecorderImportAudit(EditorPaths paths)
    {
        _root = Path.GetFullPath(Path.Combine(paths.ProjectRoot, ".recorder-audit"));
        _path = Path.Combine(_root, "audit.jsonl");
    }

    public async Task WriteAsync(
        string operation,
        string stagingId,
        string bundleId,
        string outcome,
        string? revision,
        CancellationToken cancellationToken)
    {
        var record = JsonSerializer.Serialize(new
        {
            atUtc = DateTimeOffset.UtcNow,
            operation,
            stagingId,
            bundleId,
            outcome,
            revision
        });
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_root);
            await File.AppendAllTextAsync(_path, record + "\n", Utf8, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
