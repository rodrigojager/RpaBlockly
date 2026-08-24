using System.Collections.Concurrent;

namespace RpaFlow.Packages;

public sealed record RpaPackageRegistration(
    string RpaId,
    string OriginName,
    RpaPackageOrigin Origin,
    IRpaPackageSource Source,
    IRpaPackageWriter? Writer = null);

public sealed class RpaPackageRuntimeRegistry
{
    private readonly IReadOnlyDictionary<RegistrationKey, RpaPackageRegistration>
        _registrations;
    private readonly ConcurrentDictionary<SnapshotKey, RpaPackageSnapshot> _snapshots = new();

    public RpaPackageRuntimeRegistry(IEnumerable<RpaPackageRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var values = new Dictionary<RegistrationKey, RpaPackageRegistration>();
        foreach (var registration in registrations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(registration.RpaId);
            ArgumentException.ThrowIfNullOrWhiteSpace(registration.OriginName);
            ArgumentNullException.ThrowIfNull(registration.Origin);
            ArgumentNullException.ThrowIfNull(registration.Source);
            var key = new RegistrationKey(
                Normalize(registration.RpaId),
                Normalize(registration.OriginName));
            if (!values.TryAdd(key, registration))
            {
                throw new InvalidOperationException(
                    $"O RPA '{registration.RpaId}' já possui a origem " +
                    $"'{registration.OriginName}' registrada.");
            }
        }

        _registrations = values;
    }

    public async Task<RpaPackageSnapshot> ResolveAsync(
        string rpaId,
        string originName,
        PackageRevision? revision,
        CancellationToken cancellationToken)
    {
        var registration = GetRegistration(rpaId, originName);
        if (revision is not null)
        {
            var key = new SnapshotKey(
                Normalize(rpaId),
                Normalize(originName),
                revision.Value);
            if (_snapshots.TryGetValue(key, out var cached))
            {
                return Clone(cached);
            }
        }

        var loaded = await registration.Source.LoadAsync(
            registration.RpaId,
            revision,
            cancellationToken);
        if (!loaded.RpaId.Equals(registration.RpaId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"A origem '{originName}' devolveu o RPA '{loaded.RpaId}' " +
                $"ao resolver '{registration.RpaId}'.");
        }

        var exactKey = new SnapshotKey(
            Normalize(rpaId),
            Normalize(originName),
            loaded.Revision.Value);
        var snapshot = _snapshots.GetOrAdd(exactKey, _ => CloneWithOrigin(loaded, registration));
        return Clone(snapshot);
    }

    public IRpaPackageWriter? ResolveWriter(string rpaId, string originName) =>
        GetRegistration(rpaId, originName).Writer;

    public IReadOnlyList<(string RpaId, string OriginName, RpaPackageOrigin Origin)>
        ListRegistrations() =>
        _registrations.Values
            .OrderBy(item => item.RpaId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.OriginName, StringComparer.OrdinalIgnoreCase)
            .Select(item => (item.RpaId, item.OriginName, item.Origin))
            .ToArray();

    private RpaPackageRegistration GetRegistration(string rpaId, string originName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rpaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originName);
        var key = new RegistrationKey(Normalize(rpaId), Normalize(originName));
        return _registrations.TryGetValue(key, out var registration)
            ? registration
            : throw new KeyNotFoundException(
                $"O RPA '{rpaId}' não possui a origem '{originName}' registrada.");
    }

    private static RpaPackageSnapshot CloneWithOrigin(
        RpaPackageSnapshot snapshot,
        RpaPackageRegistration registration) =>
        new(
            snapshot.RpaId,
            snapshot.Revision,
            snapshot.CopyDocuments(),
            registration.Origin);

    private static RpaPackageSnapshot Clone(RpaPackageSnapshot snapshot) =>
        new(
            snapshot.RpaId,
            snapshot.Revision,
            snapshot.CopyDocuments(),
            snapshot.Origin);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private sealed record RegistrationKey(string RpaId, string OriginName);

    private sealed record SnapshotKey(string RpaId, string OriginName, string Revision);
}
