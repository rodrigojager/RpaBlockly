using System.Diagnostics;
using System.Net;
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
        flowFile = files.FlowFileName
    }));

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

app.MapGet("/api/flow", async (
    HttpRequest request,
    EditorSession session,
    ProjectFileService files,
    CancellationToken cancellationToken) =>
{
    if (!session.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Json(await files.ReadFlowAsync(cancellationToken));
});

app.MapPut("/api/flow", async (
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
        var backup = await files.SaveFlowAsync(document, cancellationToken);
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
    Console.WriteLine($"Fluxo: {editorPaths.FlowFile}");
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
