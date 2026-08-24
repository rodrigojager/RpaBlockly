using System.Text.Json;
using RpaFlow.Contracts.V2;
using RpaFlow.Editor.Configuration;
using RpaFlow.Packages;

namespace RpaFlow.Editor.Services;

public sealed record EditorPackageDocument(
    string RpaId,
    string Revision,
    string ContentHash,
    RpaPackageOrigin Origin,
    FlowDefinition Flow,
    LocatorCatalog Locators,
    RpaPolicyDefinition Policy,
    IReadOnlyList<string> Warnings);

public sealed record EditorPackageSaveRequest(
    string ExpectedRevision,
    JsonElement Flow,
    JsonElement Locators,
    JsonElement Policy);

public sealed record EditorPackageComponentSaveRequest(
    string ExpectedRevision,
    JsonElement Document);

public sealed class PackageEditorService
{
    private readonly EditorPaths _paths;
    private readonly FileRpaPackageStore _store;

    public PackageEditorService(EditorPaths paths)
    {
        _paths = paths;
        _store = new FileRpaPackageStore(paths.PackageStoreRoot);
    }

    public async Task<EditorPackageDocument> OpenAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await _store.LoadAsync(
            _paths.RpaId,
            null,
            cancellationToken);
        return ToDocument(snapshot);
    }

    public async Task<EditorPackageDocument> SaveAsync(
        EditorPackageSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var documents = new RpaPackageDocuments(
            Deserialize<FlowDefinition>(request.Flow, "flow.production.json"),
            Deserialize<LocatorCatalog>(request.Locators, "locators.production.json"),
            Deserialize<RpaPolicyDefinition>(request.Policy, "rpa.policy.json"));
        RpaPackageValidator.Validate(documents);
        var result = await _store.PublishAsync(
            _paths.RpaId,
            documents,
            new PackageRevision(request.ExpectedRevision),
            cancellationToken);
        var saved = await _store.LoadAsync(
            _paths.RpaId,
            result.Revision,
            cancellationToken);
        return ToDocument(saved);
    }

    public Task<IReadOnlyList<PackageRevision>> ListRevisionsAsync(
        CancellationToken cancellationToken) =>
        _store.ListRevisionsAsync(_paths.RpaId, cancellationToken);

    public Task<EditorPackageDocument> SaveFlowAsync(
        EditorPackageComponentSaveRequest request,
        CancellationToken cancellationToken) =>
        SaveComponentAsync(
            request,
            documents => documents with
            {
                Flow = Deserialize<FlowDefinition>(
                    request.Document,
                    "flow.production.json")
            },
            cancellationToken);

    public Task<EditorPackageDocument> SaveLocatorsAsync(
        EditorPackageComponentSaveRequest request,
        CancellationToken cancellationToken) =>
        SaveComponentAsync(
            request,
            documents => documents with
            {
                Locators = Deserialize<LocatorCatalog>(
                    request.Document,
                    "locators.production.json")
            },
            cancellationToken);

    public Task<EditorPackageDocument> SavePolicyAsync(
        EditorPackageComponentSaveRequest request,
        CancellationToken cancellationToken) =>
        SaveComponentAsync(
            request,
            documents => documents with
            {
                Policy = Deserialize<RpaPolicyDefinition>(
                    request.Document,
                    "rpa.policy.json")
            },
            cancellationToken);

    private async Task<EditorPackageDocument> SaveComponentAsync(
        EditorPackageComponentSaveRequest request,
        Func<RpaPackageDocuments, RpaPackageDocuments> replace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expected = new PackageRevision(request.ExpectedRevision);
        var opened = await _store.LoadAsync(_paths.RpaId, expected, cancellationToken);
        var documents = replace(opened.CopyDocuments());
        RpaPackageValidator.Validate(documents);
        var result = await _store.PublishAsync(
            _paths.RpaId,
            documents,
            expected,
            cancellationToken);
        return ToDocument(await _store.LoadAsync(
            _paths.RpaId,
            result.Revision,
            cancellationToken));
    }

    private static T Deserialize<T>(JsonElement value, string description)
        where T : class =>
        V2JsonSerializer.Deserialize<T>(value.GetRawText(), description);

    private static EditorPackageDocument ToDocument(RpaPackageSnapshot snapshot)
    {
        var documents = snapshot.CopyDocuments();
        var validation = RpaPackageValidator.Validate(documents);
        return new EditorPackageDocument(
            snapshot.RpaId,
            snapshot.Revision.Value,
            snapshot.ContentHash,
            snapshot.Origin,
            documents.Flow,
            documents.Locators,
            documents.Policy,
            validation.Warnings);
    }
}
