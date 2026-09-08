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
        if (options.Schema.Length > 128 || !Identifier().IsMatch(options.Schema))
        {
            throw new ArgumentException(
                "Schema SQL inválido; use até 128 letras ASCII, números ou sublinhados.",
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
        if (options.OriginKind.Length > 100)
        {
            throw new ArgumentException(
                "OriginKind não pode exceder 100 caracteres.",
                nameof(options));
        }
        if (options.OriginLocation.Length > 1000)
        {
            throw new ArgumentException(
                "OriginLocation não pode exceder 1000 caracteres.",
                nameof(options));
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
