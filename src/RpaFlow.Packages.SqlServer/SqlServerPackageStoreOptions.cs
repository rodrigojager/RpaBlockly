using System.Text.RegularExpressions;

namespace RpaFlow.Packages.SqlServer;

public sealed record SqlServerPackageStoreOptions(
    string ConnectionString,
    string Schema = "rpa",
    int CommandTimeoutSeconds = 30,
    string OriginKind = "sqlserver",
    string OriginLocation = "database");

internal static partial class SqlServerPackageStoreOptionsValidator
{
    public static void Validate(SqlServerPackageStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        if (!Identifier().IsMatch(options.Schema))
        {
            throw new ArgumentException(
                "Schema SQL inválido; use letras ASCII, números e sublinhado.",
                nameof(options));
        }

        if (options.CommandTimeoutSeconds is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CommandTimeoutSeconds deve estar entre 1 e 600.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.OriginKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OriginLocation);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
