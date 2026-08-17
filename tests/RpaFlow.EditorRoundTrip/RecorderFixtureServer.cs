using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class RecorderFixtureServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly string _root;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _loop;
    private volatile bool _changedDom;

    private RecorderFixtureServer(HttpListener listener, string root, string baseUrl)
    {
        _listener = listener;
        _root = root;
        BaseUrl = baseUrl;
        _loop = ServeAsync();
    }

    public string BaseUrl { get; }

    public bool ChangedDom
    {
        get => _changedDom;
        set => _changedDom = value;
    }

    public static RecorderFixtureServer Start(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Fixture Recorder ausente: {fullRoot}.");
        }
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var baseUrl = $"http://127.0.0.1:{port}";
        var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl + "/");
        listener.Start();
        return new RecorderFixtureServer(listener, fullRoot, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Close();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
        _shutdown.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            _ = Task.Run(() => RespondAsync(context), CancellationToken.None);
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var fileName = path switch
            {
                "/" or "/index.html" or "/app" => "index.html",
                "/app.js" => "app.js",
                "/frame.html" => "frame.html",
                "/popup.html" => "popup.html",
                "/traditional.html" => "traditional.html",
                _ => null
            };
            if (fileName is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(_root, fileName),
                _shutdown.Token);
            if (fileName == "app.js" && _changedDom)
            {
                bytes = Encoding.UTF8.GetBytes(
                    Encoding.UTF8.GetString(bytes) +
                    "\ndocument.querySelector('#dynamic-action')?.removeAttribute('data-testid');\n");
            }
            context.Response.ContentType = fileName.EndsWith(".js", StringComparison.Ordinal)
                ? "text/javascript; charset=utf-8"
                : "text/html; charset=utf-8";
            context.Response.Headers.Add("Cache-Control", "no-store");
            context.Response.ContentLength64 = bytes.LongLength;
            await context.Response.OutputStream.WriteAsync(bytes, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            context.Response.Close();
        }
    }
}
