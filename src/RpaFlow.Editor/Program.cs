using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using RpaFlow.Editor.Configuration;
using RpaFlow.Editor.Infrastructure;
using RpaFlow.Editor.Security;
using RpaFlow.Editor.Services;

var editorArguments = EditorArguments.Parse(args);
var editorPaths = ProjectRootResolver.Resolve(editorArguments);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
builder.WebHost.UseUrls(editorArguments.Url);
builder.Services.AddSingleton(editorPaths);
builder.Services.AddSingleton<EditorSession>();
builder.Services.AddSingleton<ProjectFileService>();
builder.Services.AddSingleton<PackageEditorService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(
        JsonNamingPolicy.CamelCase,
        allowIntegerValues: false));
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var remoteAddress = context.Connection.RemoteIpAddress;
    if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/session", (EditorSession session, ProjectFileService files) =>
    Results.Json(new
    {
        token = session.Token,
        profile = editorPaths.Profile,
        configurationFile = files.ConfigurationFileName,
        package = new
        {
            rpaId = editorPaths.RpaId,
            storeRoot = Path.GetRelativePath(
                editorPaths.ProjectRoot,
                editorPaths.PackageStoreRoot)
        }
    }));

app.MapGet("/api/package", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    CancellationToken cancellationToken) =>
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    try
    {
        return Results.Json(await packages.OpenAsync(cancellationToken));
    }
    catch (Exception exception) when (exception is InvalidOperationException or
                                             KeyNotFoundException or
                                             FileNotFoundException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/api/package", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    EditorPackageSaveRequest document,
    CancellationToken cancellationToken) =>
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    try
    {
        return Results.Json(await packages.SaveAsync(document, cancellationToken));
    }
    catch (RpaFlow.Packages.PackageRevisionConflictException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (Exception exception) when (exception is InvalidOperationException or
                                             System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/package/revisions", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    CancellationToken cancellationToken) =>
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    var revisions = await packages.ListRevisionsAsync(cancellationToken);
    return Results.Json(revisions.Select(item => item.Value));
});

app.MapGet("/api/flow", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    CancellationToken cancellationToken) =>
    await ReadPackageComponentAsync(
        request,
        session,
        packages,
        document => document.Flow,
        cancellationToken));

app.MapGet("/api/locators", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    CancellationToken cancellationToken) =>
    await ReadPackageComponentAsync(
        request,
        session,
        packages,
        document => document.Locators,
        cancellationToken));

app.MapGet("/api/policy", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    CancellationToken cancellationToken) =>
    await ReadPackageComponentAsync(
        request,
        session,
        packages,
        document => document.Policy,
        cancellationToken));

app.MapPut("/api/flow", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    EditorPackageComponentSaveRequest document,
    CancellationToken cancellationToken) =>
    await SavePackageComponentAsync(
        request,
        session,
        () => packages.SaveFlowAsync(document, cancellationToken)));

app.MapPut("/api/locators", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    EditorPackageComponentSaveRequest document,
    CancellationToken cancellationToken) =>
    await SavePackageComponentAsync(
        request,
        session,
        () => packages.SaveLocatorsAsync(document, cancellationToken)));

app.MapPut("/api/policy", async (
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    EditorPackageComponentSaveRequest document,
    CancellationToken cancellationToken) =>
    await SavePackageComponentAsync(
        request,
        session,
        () => packages.SavePolicyAsync(document, cancellationToken)));

app.MapGet("/api/configuration", async (
    HttpRequest request,
    EditorSession session,
    ProjectFileService files,
    CancellationToken cancellationToken) =>
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Json(await files.ReadConfigurationAsync(cancellationToken));
});

app.MapPut("/api/configuration", async (
    HttpRequest request,
    EditorSession session,
    ProjectFileService files,
    System.Text.Json.JsonElement document,
    CancellationToken cancellationToken) =>
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    try
    {
        var backup = await files.SaveConfigurationAsync(document, cancellationToken);
        return Results.Json(new { saved = true, backupFile = Path.GetFileName(backup) });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapFallbackToFile("index.html");

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"Editor disponível em {editorArguments.Url}");
    Console.WriteLine($"RPA: {editorPaths.Profile.DisplayName}");
    Console.WriteLine($"Configuração: {editorPaths.ConfigurationFile}");
    Console.WriteLine(
        $"Pacote: {editorPaths.RpaId} em {editorPaths.PackageStoreRoot}");
    Console.WriteLine("Feche esta janela ou pressione Ctrl+C para encerrar.");

    if (editorArguments.OpenBrowser)
    {
        Process.Start(new ProcessStartInfo(editorArguments.Url)
        {
            UseShellExecute = true
        });
    }
});

await app.RunAsync();

static async Task<IResult> ReadPackageComponentAsync<T>(
    HttpRequest request,
    EditorSession session,
    PackageEditorService packages,
    Func<EditorPackageDocument, T> select,
    CancellationToken cancellationToken)
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    try
    {
        var package = await packages.OpenAsync(cancellationToken);
        return Results.Json(new
        {
            package.RpaId,
            package.Revision,
            package.ContentHash,
            Document = select(package)
        });
    }
    catch (Exception exception) when (exception is InvalidOperationException or
                                             KeyNotFoundException or
                                             FileNotFoundException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
}

static async Task<IResult> SavePackageComponentAsync(
    HttpRequest request,
    EditorSession session,
    Func<Task<EditorPackageDocument>> save)
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    try
    {
        return Results.Json(await save());
    }
    catch (RpaFlow.Packages.PackageRevisionConflictException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (Exception exception) when (exception is InvalidOperationException or
                                             System.Text.Json.JsonException or
                                             KeyNotFoundException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
}
