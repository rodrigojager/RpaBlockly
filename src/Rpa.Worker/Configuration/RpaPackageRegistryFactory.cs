using RpaFlow.Packages;
using RpaFlow.Packages.SqlServer;

namespace Rpa.Worker.Configuration;

public static class RpaPackageRegistryFactory
{
    public static RpaPackageRuntimeRegistry Create(
        RpaWorkerOptions options,
        WorkerEnvironment environment)
    {
        var registrations = new List<RpaPackageRegistration>();
        foreach (var (code, definition) in options.Definitions)
        {
            if (definition.Package is null)
            {
                continue;
            }

            var package = definition.Package;
            var rpaId = string.IsNullOrWhiteSpace(package.RpaId) ? code : package.RpaId;
            registrations.Add(CreateRegistration(
                rpaId,
                package.OriginName,
                package.Provider,
                package.Location,
                options.Tables.Schema,
                environment));
            if (package.Overlay is not null)
            {
                registrations.Add(CreateRegistration(
                    rpaId,
                    package.Overlay.OriginName,
                    package.Overlay.Provider,
                    package.Overlay.Location,
                    options.Tables.Schema,
                    environment));
            }
        }

        return new RpaPackageRuntimeRegistry(registrations);
    }

    private static RpaPackageRegistration CreateRegistration(
        string rpaId,
        string originName,
        string provider,
        string location,
        string schema,
        WorkerEnvironment environment)
    {
        return provider.ToLowerInvariant() switch
        {
            "file" => CreateFile(rpaId, originName, location, environment),
            "sqlserver" => CreateSqlServer(rpaId, originName, location, schema, environment),
            _ => throw new InvalidOperationException(
                $"Provider de pacote não suportado: '{provider}'.")
        };
    }

    private static RpaPackageRegistration CreateFile(
        string rpaId,
        string originName,
        string location,
        WorkerEnvironment environment)
    {
        var root = RpaWorkerOptionsValidator.ResolvePath(
            environment.Paths.WorkspaceRoot,
            location);
        var store = new FileRpaPackageStore(root);
        return new RpaPackageRegistration(
            rpaId,
            originName,
            new RpaPackageOrigin("file", root),
            store,
            store);
    }

    private static RpaPackageRegistration CreateSqlServer(
        string rpaId,
        string originName,
        string location,
        string schema,
        WorkerEnvironment environment)
    {
        var store = new SqlServerRpaPackageStore(new SqlServerPackageStoreOptions(
            environment.ConnectionString,
            Schema: schema,
            OriginLocation: location));
        return new RpaPackageRegistration(
            rpaId,
            originName,
            new RpaPackageOrigin("sqlserver", location),
            store,
            store);
    }
}
