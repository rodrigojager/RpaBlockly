using System.Net;
using System.Text;

var fixtureArguments = FixtureArguments.Parse(args);
if (fixtureArguments.ShowHelp)
{
    Console.WriteLine(
        "Uso: dotnet run --project tools/RpaFlow.RecorderFixture -- " +
        "[--port 5178] [--changed-dom]");
    return;
}

var fixtureRoot = FindFixtureRoot();
var strictUtf8 = new UTF8Encoding(false, true);
var files = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["/"] = "index.html",
    ["/index.html"] = "index.html",
    ["/app"] = "index.html",
    ["/app.js"] = "app.js",
    ["/frame.html"] = "frame.html",
    ["/popup.html"] = "popup.html",
    ["/traditional.html"] = "traditional.html"
};

foreach (var fileName in files.Values.Distinct(StringComparer.Ordinal))
{
    _ = strictUtf8.GetString(File.ReadAllBytes(Path.Combine(fixtureRoot, fileName)));
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = []
});
builder.WebHost.UseUrls($"http://127.0.0.1:{fixtureArguments.Port}");
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

app.MapGet("/{**path}", async context =>
{
    var path = context.Request.Path.Value ?? "/";
    if (!files.TryGetValue(path, out var fileName))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var bytes = await File.ReadAllBytesAsync(
        Path.Combine(fixtureRoot, fileName),
        context.RequestAborted);
    if (fileName == "app.js" && fixtureArguments.ChangedDom)
    {
        var script = strictUtf8.GetString(bytes) +
            "\ndocument.querySelector('#dynamic-action')" +
            "?.removeAttribute('data-testid');\n";
        bytes = strictUtf8.GetBytes(script);
    }

    context.Response.ContentType = fileName.EndsWith(".js", StringComparison.Ordinal)
        ? "text/javascript; charset=utf-8"
        : "text/html; charset=utf-8";
    context.Response.ContentLength = bytes.LongLength;
    await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine(
        $"Fixture Recorder disponível em http://127.0.0.1:{fixtureArguments.Port}/index.html");
    Console.WriteLine(fixtureArguments.ChangedDom
        ? "Modo: DOM alterado para comprovar fallback."
        : "Modo: DOM original para execução strict.");
    Console.WriteLine("Pressione Ctrl+C para encerrar.");
});

await app.RunAsync();

static string FindFixtureRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tests",
                "fixtures",
                "recorder-site");
            if (File.Exists(Path.Combine(candidate, "index.html")) &&
                File.Exists(Path.Combine(candidate, "app.js")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "A fixture tests/fixtures/recorder-site não foi encontrada. " +
        "Execute a ferramenta dentro do clone da RpaBlockly.");
}

internal sealed record FixtureArguments(int Port, bool ChangedDom, bool ShowHelp)
{
    public static FixtureArguments Parse(string[] arguments)
    {
        var port = 5178;
        var changedDom = false;
        var showHelp = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index].ToLowerInvariant())
            {
                case "--port":
                    if (index + 1 >= arguments.Length ||
                        !int.TryParse(arguments[++index], out port) ||
                        port is < 1024 or > 65535)
                    {
                        throw new ArgumentException(
                            "--port deve ser um inteiro entre 1024 e 65535.");
                    }
                    break;
                case "--changed-dom":
                    changedDom = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException(
                        $"Argumento desconhecido: {arguments[index]}.");
            }
        }

        return new FixtureArguments(port, changedDom, showHelp);
    }
}
