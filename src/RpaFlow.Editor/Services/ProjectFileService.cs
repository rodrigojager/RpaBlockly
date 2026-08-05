using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RpaFlow.Editor.Configuration;
using RpaFlow.Editor.Validation;

namespace RpaFlow.Editor.Services;

public sealed class ProjectFileService(EditorPaths paths)
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public string ConfigurationFileName => Path.GetFileName(paths.ConfigurationFile);

    public string FlowFileName => Path.GetFileName(paths.FlowFile);

    public Task<JsonElement> ReadConfigurationAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            paths.ConfigurationFile,
            document => ConfigurationDocumentValidator.Validate(document, paths.Profile),
            cancellationToken);

    public Task<JsonElement> ReadFlowAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            paths.FlowFile,
            FlowDocumentValidator.Validate,
            cancellationToken);

    public Task<string> SaveConfigurationAsync(
        JsonElement document,
        CancellationToken cancellationToken) =>
        SaveAsync(
            paths.ConfigurationFile,
            document,
            value => ConfigurationDocumentValidator.Validate(value, paths.Profile),
            cancellationToken);

    public Task<string> SaveFlowAsync(
        JsonElement document,
        CancellationToken cancellationToken) =>
        SaveAsync(
            paths.FlowFile,
            document,
            FlowDocumentValidator.Validate,
            cancellationToken);

    private async Task<JsonElement> ReadAsync(
        string path,
        Action<JsonElement> validate,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var json = StrictUtf8.GetString(bytes);
            using var document = JsonDocument.Parse(json);
            validate(document.RootElement);
            return document.RootElement.Clone();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<string> SaveAsync(
        string path,
        JsonElement document,
        Action<JsonElement> validate,
        CancellationToken cancellationToken)
    {
        validate(document);
        var json = JsonSerializer.Serialize(document, WriteOptions) + Environment.NewLine;
        var temporaryPath = path + ".tmp";
        var backupPath = path + ".bak";

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                StrictUtf8,
                cancellationToken);

            var verificationBytes = await File.ReadAllBytesAsync(temporaryPath, cancellationToken);
            using var verificationDocument = JsonDocument.Parse(
                StrictUtf8.GetString(verificationBytes));
            validate(verificationDocument.RootElement);

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            return backupPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _fileLock.Release();
        }
    }
}
