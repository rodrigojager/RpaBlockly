using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rpa.Worker.Authentication;
using Rpa.Worker.Configuration;
using Rpa.Worker.Data;
using Rpa.Worker.Execution;
using RpaFlow.Runtime;

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var configurationPath = Path.GetFullPath(
    ResolveArgument(args, "--config", ResolveDefaultConfigurationPath()));
if (!File.Exists(configurationPath))
{
    throw new FileNotFoundException("Configuração do worker não encontrada.", configurationPath);
}

var configurationDirectory =
    Path.GetDirectoryName(configurationPath) ?? Directory.GetCurrentDirectory();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = configurationDirectory
});
builder.Configuration.AddJsonFile(configurationPath, optional: false, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

var options = new RpaWorkerOptions();
builder.Configuration.GetSection(RpaWorkerOptions.SectionName).Bind(options);
var connectionString =
    builder.Configuration.GetConnectionString("RpaDatabase") ?? string.Empty;
var paths = RpaWorkerOptionsValidator.Validate(
    options,
    configurationDirectory,
    connectionString);
var workerEnvironment = new WorkerEnvironment(connectionString, paths);
var packageRegistry = RpaPackageRegistryFactory.Create(options, workerEnvironment);
await RpaWorkerOptionsValidator.ValidateFlowsAsync(
    options,
    paths,
    cancellationSource.Token,
    packageRegistry);

if (args.Contains("--validate-only", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(
        $"Worker válido: {options.Definitions.Count} definição(ões), " +
        $"modo {options.ExecutionMode}, habilitado={options.Enabled}.");
    return;
}

if (!options.Enabled)
{
    Console.WriteLine(
        "O worker está validado, mas desabilitado. Defina RpaWorker.Enabled=true " +
        "somente depois de configurar banco, claims e limites de segurança.");
    return;
}

Directory.CreateDirectory(paths.ArtifactRoot);
Directory.CreateDirectory(paths.SessionStateRoot);

builder.Services.AddWindowsService(settings =>
    settings.ServiceName = "RPA Blockly Worker");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(workerEnvironment);
builder.Services.AddSingleton(packageRegistry);
builder.Services.AddSingleton<IOneTimeCodeProvider, MicrosoftGraphEmailOneTimeCodeProvider>();
builder.Services.AddSingleton<SqlWorkItemRepository>();
builder.Services.AddSingleton<WorkItemProcessor>();
builder.Services.AddHostedService<RpaBackgroundService>();

await builder.Build().RunAsync(cancellationSource.Token);

static string ResolveDefaultConfigurationPath()
{
    var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory())
        ?? FindRepositoryRoot(AppContext.BaseDirectory);
    if (repositoryRoot is not null)
    {
        var sourceDirectory = Path.Combine(repositoryRoot, "src", "Rpa.Worker");
        var sourceLocal = Path.Combine(sourceDirectory, "appsettings.local.json");
        return File.Exists(sourceLocal)
            ? sourceLocal
            : Path.Combine(sourceDirectory, "appsettings.example.json");
    }

    var local = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");
    return File.Exists(local)
        ? local
        : Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");
}

static string? FindRepositoryRoot(string startDirectory)
{
    var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "RpaBlockly.slnx")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}

static string ResolveArgument(string[] arguments, string name, string defaultValue)
{
    var index = Array.FindIndex(
        arguments,
        value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return defaultValue;
    }

    if (index + 1 >= arguments.Length)
    {
        throw new ArgumentException($"Informe um caminho depois de {name}.");
    }

    return arguments[index + 1];
}
